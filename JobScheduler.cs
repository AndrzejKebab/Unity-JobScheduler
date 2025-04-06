using System;
using Cysharp.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;

namespace PatataGames.JobScheduler
{
	/// <summary>
	///     Data structure for IJob implementations to be scheduled.
	/// </summary>
	/// <typeparam name="T">The job type, which must be a struct implementing IJob.</typeparam>
	public struct JobData<T> : IJobData where T: struct, IJob
	{
		/// <summary>
		///     The job to be scheduled.
		/// </summary>
		public T         Job;
		
		/// <summary>
		///     Optional job handle that must complete before this job can start.
		/// </summary>
		public JobHandle Dependency;

		public Guid JobID;
		
		/// <summary>
		///     Schedules the job with the specified dependency.
		/// </summary>
		/// <returns>A JobHandle that can be used to track the job's completion.</returns>
		public JobHandle Schedule()
		{
			return Job.ScheduleByRef(Dependency);
		}
	}
	
	/// <summary>
	///     Specialized scheduler for IJob implementations.
	///     Provides batched scheduling and completion of jobs with yielding to prevent main thread blocking.
	/// </summary>
	/// <typeparam name="T">The job type, which must be an unmanaged struct implementing IJob.</typeparam>
	public struct JobScheduler<T> : IDisposable
		where T : unmanaged, IJob
	{
		private JobSchedulerBase    baseScheduler;
		private NativeQueue<JobData<T>> jobsQueue;

		/// <summary>
		///     Initializes a new instance of the JobScheduler struct.
		/// </summary>
		/// <param name="capacity">Initial capacity for the job list. Default is 64.</param>
		/// <param name="batchSize">Number of jobs to process before yielding. Default is 32.</param>
		public JobScheduler(int capacity = 64,byte batchSize = 32)
		{
			baseScheduler = new JobSchedulerBase(capacity, batchSize);
			jobsQueue = new NativeQueue<JobData<T>>(Allocator.Persistent);
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
		public int JobsToSchedule => jobsQueue.Count;

		/// <summary>
		/// Gets the total number of jobs being managed by the scheduler.
		/// </summary>
		/// <value>The sum of jobs waiting to be scheduled and jobs that are currently running but not completed.</value>
		public int JobsCount => JobsToSchedule + ScheduledJobs;

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
		public void AddJob(T job, JobHandle dependency = default)
		{
			var id = Guid.NewGuid();
			jobsQueue.Enqueue(new JobData<T>
			             {
				             Job       = job,
				             Dependency = dependency,
				             JobID = id
			             });
		}

		/// <summary>
		///     Adds an external job handle to the tracking list.
		/// </summary>
		/// <param name="handle">The job handle to track.</param>
		public void AddJobHandle(JobHandle handle, Guid jobId) => baseScheduler.AddJobHandle(handle, jobId);

		/// <summary>
		///     Schedules all queued jobs in batches, yielding between batches to prevent
		///     blocking the main thread for too long.
		/// </summary>
		/// <returns>A UniTask that completes when all jobs are scheduled.</returns>
		public async UniTask ScheduleJobsAsync()
		{
			byte count = 0;

			for (var i = 0; i < jobsQueue.Count; i++)
			{
				count++;
				var       job    = jobsQueue.Dequeue();
				JobHandle handle = job.Schedule();
				baseScheduler.AddJobHandle(handle, job.JobID);

				if (count < BatchSize) continue;
				await UniTask.Yield();
				count = 0;
			}

			jobsQueue.Clear();

			if (count > 0) await UniTask.Yield();
		}

		/// <summary>
		///     Completes all tracked jobs in batches, yielding between batches to prevent
		///     blocking the main thread for too long.
		/// </summary>
		/// <returns>A UniTask that completes when all jobs are finished.</returns>
		public UniTask CompleteAsync() => baseScheduler.CompleteAsync();

		/// <summary>
		///     Completes all tracked jobs without yielding.
		///     Use this when immediate completion is required.
		/// </summary>
		public void CompleteImmediate() => baseScheduler.CompleteImmediate();

		/// <summary>
		///     Completes all jobs and releases resources.
		/// </summary>
		public void Dispose()
		{
			baseScheduler.Dispose();
			jobsQueue.Dispose();
		}
	}
}