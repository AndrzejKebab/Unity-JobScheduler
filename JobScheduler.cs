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
	public struct JobScheduler<T> : IJobScheduler, IDisposable
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
		/// Gets the number of jobs that have been scheduled.
		/// </summary>
		/// <value>The count of uncompleted job handles being tracked by the base scheduler.</value>
		public int ScheduledJobs => baseScheduler.JobHandlesCount;

		/// <summary>
		/// Gets the number of jobs in the queue waiting to be scheduled.
		/// </summary>
		/// <value>The length of the jobs list that contains pending jobs.</value>
		public int JobsToSchedule => jobsList.Length;

		/// <summary>
		/// Gets the total number of jobs being managed by the scheduler.
		/// </summary>
		/// <value>The sum of jobs waiting to be scheduled and jobs that are currently running but not completed.</value>
		public int JobsCount => jobsList.Length + baseScheduler.JobHandlesCount;

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
		public void AddJobHandle(JobHandle handle) => baseScheduler.AddJobHandle(handle);

		/// <summary>
		///     Schedules all queued jobs in batches, yielding between batches to prevent
		///     blocking the main thread for too long.
		/// </summary>
		/// <returns>A UniTask that completes when all jobs are scheduled.</returns>
		[BurstCompile]
		public async UniTask ScheduleJobsAsync()
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
		public UniTask CompleteAsync() => baseScheduler.CompleteAsync();

		/// <summary>
		///     Completes all tracked jobs without yielding.
		///     Use this when immediate completion is required.
		/// </summary>
		[BurstCompile]
		public void CompleteImmediate() => baseScheduler.CompleteImmediate();

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