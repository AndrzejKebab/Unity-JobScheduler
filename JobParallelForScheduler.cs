using System;
using Cysharp.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;

namespace PatataGames.JobScheduler
{
	/// <summary>
	///     Data structure for IJobParallelFor implementations to be scheduled.
	/// </summary>
	/// <typeparam name="T">The job type, which must be a struct implementing IJobParallelFor.</typeparam>
	public struct JobParallelForData<T> : IJobData where T: struct, IJobParallelFor
	{
		/// <summary>
		///     The job to be scheduled.
		/// </summary>
		public T         Job;
		
		/// <summary>
		///     The length of the array to process.
		/// </summary>
		public int       ArrayLength;
		
		/// <summary>
		///     The batch size for each worker thread.
		/// </summary>
		public int       InnerBatchSize;
		
		/// <summary>
		///     Optional job handle that must complete before this job can start.
		/// </summary>
		public JobHandle Dependency;
		
		/// <summary>
		///     Schedules the job with the specified array length, inner batch size, and dependency.
		/// </summary>
		/// <returns>A JobHandle that can be used to track the job's completion.</returns>
		public JobHandle Schedule()
		{
			return Job.Schedule(ArrayLength, InnerBatchSize, Dependency);
		}
	}
	
	/// <summary>
	///     Specialized scheduler for IJobParallelFor implementations.
	///     Provides batched scheduling and completion of jobs with yielding to prevent main thread blocking.
	/// </summary>
	/// <typeparam name="T">The job type, which must be an unmanaged struct implementing IJobParallelFor.</typeparam>
	public struct JobParallelForScheduler<T> : IJobScheduler, IDisposable
		where T : unmanaged, IJobParallelFor
	{
		private JobSchedulerBase                  baseScheduler;
		private NativeQueue<JobParallelForData<T>> jobsQueue;
		
		/// <summary>
		///     Initializes a new instance of the JobParallelForScheduler struct.
		/// </summary>
		/// <param name="capacity">Initial capacity for the job list. Default is 64.</param>
		/// <param name="batchSize">Number of jobs to process before yielding. Default is 32.</param>
		public JobParallelForScheduler(int capacity = 64, byte batchSize = 32)
		{
			baseScheduler = new JobSchedulerBase(capacity, batchSize);
			jobsQueue = new NativeQueue<JobParallelForData<T>>(Allocator.Persistent);
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
		public int ScheduledJobs => baseScheduler.JobHandlesCount;

		/// <summary>
		///     Returns the number of jobs in the queue waiting to be scheduled.
		/// </summary>
		public int JobsToSchedule => jobsQueue.Count;
		
		/// <summary>
		///     Returns the total number of jobs being managed by the scheduler.
		///     This includes both jobs waiting to be scheduled and jobs that are currently running.
		/// </summary>
		public int JobsCount => ScheduledJobs + JobsToSchedule;

		/// <summary>
		///     Checks if all scheduled jobs have been completed.
		/// </summary>
		/// <value>
		///     <c>true</c> if all jobs are completed; otherwise, <c>false</c> if any job is still running.
		/// </value>
		public bool AreJobsCompleted => baseScheduler.AreJobsCompleted;

		/// <summary>
		///     Adds a job to the queue for scheduling with the specified array length and inner batch size.
		/// </summary>
		/// <param name="job">The job to add.</param>
		/// <param name="arrayLength">The length of the array to process.</param>
		/// <param name="innerBatchSize">The batch size for each worker thread. Default is 64.</param>
		/// <param name="dependency">Optional job handle that must complete before this job can start.</param>
		public void AddJob(T job, int arrayLength, int innerBatchSize = 64, JobHandle dependency = default)
		{
			jobsQueue.Enqueue(new JobParallelForData<T>
			             {
				             Job            = job,
				             ArrayLength    = arrayLength,
				             InnerBatchSize = innerBatchSize,
				             Dependency     = dependency
			             });
		}

		/// <summary>
		///     Adds an external job handle to the tracking list.
		/// </summary>
		/// <param name="handle">The job handle to track.</param>
		public void AddJobHandle(JobHandle handle)
		{
			baseScheduler.AddJobHandle(handle);
		}

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
				JobParallelForData<T> data   = jobsQueue.Dequeue();
				JobHandle            handle = data.Job.ScheduleByRef(data.ArrayLength, data.InnerBatchSize, data.Dependency);
				baseScheduler.AddJobHandle(handle);

				if (count < BatchSize) continue;
				await UniTask.Yield();
				count = 0;
			}

			jobsQueue.Clear();
		}

		/// <summary>
		///     Completes all tracked jobs in batches, yielding between batches to prevent
		///     blocking the main thread for too long.
		/// </summary>
		/// <returns>A UniTask that completes when all jobs are finished.</returns>
		public UniTask CompleteAsync()
		{
			return baseScheduler.CompleteAsync();
		}

		/// <summary>
		///     Completes all tracked jobs without yielding.
		///     Use this when immediate completion is required.
		/// </summary>
		public void CompleteImmediate()
		{
			baseScheduler.CompleteImmediate();
		}

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