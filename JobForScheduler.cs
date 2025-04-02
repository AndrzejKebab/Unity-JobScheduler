using System;
using Cysharp.Threading.Tasks;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace PatataGames.JobScheduler
{
	[BurstCompile]
	public struct JobForData<T> : IJobData where T : struct, IJobFor
	{
		public T         Job;
		public int       ArrayLength;
		public JobHandle Dependency;
		
		public JobHandle Schedule()
		{
			return Job.Schedule(ArrayLength, Dependency);
		}
	}
	
	/// <summary>
	///     Specialized scheduler for IJobFor implementations.
	///     Provides batched scheduling and completion of jobs with yielding to prevent main thread blocking.
	/// </summary>
	/// <typeparam name="T">The job type, which must be an unmanaged struct implementing IJobFor.</typeparam>
	[BurstCompile]
	public struct JobForScheduler<T> : IDisposable
		where T : unmanaged, IJobFor
	{
		private JobSchedulerBase       baseScheduler;
		private NativeList<JobForData<T>> jobList;

		public JobForScheduler(int capacity = 64, byte batchSize = 8)
		{
			baseScheduler = new JobSchedulerBase(capacity, batchSize);
			jobList = new NativeList<JobForData<T>>(capacity, Allocator.Persistent);
		}

		/// <summary>
		///     Controls how many jobs are processed before yielding back to the main thread.
		///     Default is 8.
		/// </summary>
		public byte BatchSize
		{
			get => baseScheduler.BatchSize;
			set => baseScheduler.BatchSize = value;
		}

		/// <summary>
		///     Returns the number of tracked job handles.
		/// </summary>
		public int JobHandlesCount => baseScheduler.JobHandlesCount;

		/// <summary>
		///     Returns the number of jobs in the queue waiting to be scheduled.
		/// </summary>
		public int JobsCount => jobList.Length;

		/// <summary>
		///     Checks if all scheduled jobs have been completed.
		/// </summary>
		/// <value>
		///     <c>true</c> if all jobs are completed; otherwise, <c>false</c> if any job is still running.
		/// </value>
		public bool AreJobsCompleted => baseScheduler.AreJobsCompleted;
		
		/// <summary>
		///     Adds a job to the queue for scheduling with the specified array length.
		/// </summary>
		/// <param name="job">The job to add.</param>
		/// <param name="arrayLength">The length of the array to process.</param>
		/// <param name="dependency">Optional job handle that must complete before this job can start.</param>
		[BurstCompile]
		public void AddJob(T job, int arrayLength, JobHandle dependency = default)
		{
			jobList.Add(new JobForData<T>
			             {
				             Job         = job,
				             ArrayLength = arrayLength,
				             Dependency   = dependency
			             });
		}

		/// <summary>
		///     Adds an external job handle to the tracking list.
		/// </summary>
		/// <param name="handle">The job handle to track.</param>
		[BurstCompile]
		public void ScheduleJob(JobHandle handle)
		{
			baseScheduler.AddJobHandle(handle);
		}

		/// <summary>
		///     Schedules all queued jobs in batches, yielding between batches to prevent
		///     blocking the main thread for too long.
		/// </summary>
		/// <returns>A UniTask that completes when all jobs are scheduled.</returns>
		[BurstCompile]
		public async UniTask ScheduleAll()
		{
			byte count = 0;

			foreach (JobForData<T> data in jobList)
			{
				count++;
				JobHandle handle = data.Job.Schedule(data.ArrayLength, data.Dependency);
				baseScheduler.AddJobHandle(handle);

				if (count < BatchSize) continue;
				await UniTask.Yield();
				count = 0;
			}

			jobList.Clear();

			if (count > 0) await UniTask.Yield();
		}

		/// <summary>
		///     Completes all tracked jobs in batches, yielding between batches to prevent
		///     blocking the main thread for too long.
		/// </summary>
		/// <returns>A UniTask that completes when all jobs are finished.</returns>
		[BurstCompile]
		public UniTask Complete()
		{
			return baseScheduler.Complete();
		}


		/// <summary>
		///     Completes all tracked jobs without yielding.
		///     Use this when immediate completion is required.
		/// </summary>
		[BurstCompile]
		public void CompleteAll()
		{
			baseScheduler.CompleteAll();
		}

		/// <summary>
		///     Completes all jobs and releases resources.
		/// </summary>
		public void Dispose()
		{
			baseScheduler.Dispose();
			jobList.Dispose();
		}
	}
}