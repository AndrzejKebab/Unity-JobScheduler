using System;
using Cysharp.Threading.Tasks;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace PatataGames.JobScheduler
{
	[BurstCompile]
	public struct JobData<T> : IJobData where T: struct, IJob
	{
		public T         Job;
		public JobHandle Dependency;
		
		public JobHandle Schedule()
		{
			return Job.Schedule(Dependency);
		}
	}
	
	/// <summary>
	///     Specialized scheduler for IJob implementations.
	///     Provides batched scheduling and completion of jobs with yielding to prevent main thread blocking.
	/// </summary>
	/// <typeparam name="T">The job type, which must be an unmanaged struct implementing IJob.</typeparam>
	[BurstCompile]
	public struct JobScheduler<T> : IDisposable
		where T : unmanaged, IJob
	{
		private JobSchedulerBase    baseScheduler;
		private NativeList<JobData<T>> jobsList;

		public JobScheduler(int capacity = 64,byte batchSize = 8)
		{
			baseScheduler = new JobSchedulerBase(capacity, batchSize);
			jobsList = new NativeList<JobData<T>>(capacity, Allocator.Persistent);
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
		public int JobsCount => jobsList.Length;

		/// <summary>
		///     Checks if all scheduled jobs have been completed.
		/// </summary>
		/// <value>
		///     <c>true</c> if all jobs are completed; otherwise, <c>false</c> if any job is still running.
		/// </value>
		public bool AreJobsCompleted => baseScheduler.AreJobsCompleted;
		
		/// <summary>
		///     Adds a job to the queue for scheduling.
		/// </summary>
		/// <param name="job">The job to add.</param>
		/// <param name="dependency">Optional job handle that must complete before this job can start.</param>
		[BurstCompile]
		public void AddJob(T job, JobHandle dependency = default)
		{
			jobsList.Add(new JobData<T>
			             {
				             Job       = job,
				             Dependency = dependency
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

			foreach (JobData<T> data in jobsList)
			{
				count++;
				JobHandle handle = data.Job.Schedule(data.Dependency);
				baseScheduler.AddJobHandle(handle);

				if (count < BatchSize) continue;
				await UniTask.Yield();
				count = 0;
			}

			jobsList.Clear();

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
			jobsList.Dispose();
		}
	}
}