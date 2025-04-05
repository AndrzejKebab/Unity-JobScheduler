using System;
using Cysharp.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace PatataGames.JobScheduler
{
	/// <summary>
	///     Base job scheduler that provides common functionality for managing Unity job handles.
	///     Handles batched completion of jobs with yielding to prevent main thread blocking.
	/// </summary>
	public struct JobSchedulerBase : IDisposable
	{
		private NativeList<JobHandle> jobHandles;
		private byte                  batchSize;
 
		/// <summary>
        /// Delegate for job-related callbacks.
        /// </summary>
        /// <param name="jobId">The identifier for the job.</param>
        public delegate void JobCallback(int jobId);
		
        public delegate void JobCompleteCallback();

        /// <summary>
        /// Delegate for batch completion callbacks.
        /// </summary>
        /// <param name="completedCount">Number of jobs completed in this batch.</param>
        /// <param name="remainingCount">Number of jobs remaining after this batch.</param>
        public delegate void BatchCompletedCallback(int completedCount, int remainingCount);

        /// <summary>
        /// Delegate for job error callbacks.
        /// </summary>
        /// <param name="jobId">The identifier for the job that encountered an error.</param>
        /// <param name="exception">The exception that occurred.</param>
        public delegate void JobErrorCallback(int jobId, Exception exception);

        /// <summary>
        /// Invoked when a new job is added to the scheduler.
        /// </summary>
        public JobCallback OnJobAdded;

        /// <summary>
        /// Invoked when a specific job completes successfully.
        /// </summary>
        public JobCompleteCallback OnJobCompleted;

        /// <summary>
        /// Invoked when a batch of jobs completes.
        /// </summary>
        public BatchCompletedCallback OnBatchCompleted;

        /// <summary>
        /// Invoked when all scheduled jobs have completed.
        /// </summary>
        public Action OnAllJobsCompleted;

        /// <summary>
        /// Invoked when a job encounters an error during completion.
        /// </summary>
        public JobErrorCallback OnJobError;
        
		/// <summary>
		///     Initializes a new instance of the JobSchedulerBase struct.
		/// </summary>
		/// <param name="capacity">Initial capacity for the job handles list. Default is 64.</param>
		/// <param name="batchSize">Number of jobs to process before yielding. Default is 32.</param>
		public JobSchedulerBase(int capacity = 64, byte batchSize = 32)
		{
			jobHandles     = new NativeList<JobHandle>(capacity, Allocator.Persistent);
			this.batchSize = batchSize;
			// Initialize callbacks as null
			OnJobAdded         = null;
			OnJobCompleted     = null;
			OnBatchCompleted   = null;
			OnAllJobsCompleted = null;
			OnJobError         = null;
		}

		/// <summary>
		///     Controls how many jobs are processed before yielding back to the main thread.
		///     Default is 32.
		/// </summary>
		public byte BatchSize
		{
			get => batchSize;
			set => batchSize = value <= 0 ? (byte)32 : value;
		}

		/// <summary>
		///     Adds a job handle to the tracking list.
		/// </summary>
		/// <param name="handle">The job handle to track.</param>
		public void AddJobHandle(JobHandle handle)
		{
			jobHandles.Add(handle);
			// Trigger the OnJobAdded callback
			OnJobAdded?.Invoke();
		}

		/// <summary>
		///     Completes all tracked jobs in batches, yielding between batches to prevent
		///     blocking the main thread for too long.
		/// </summary>
		/// <returns>A UniTask that completes when all jobs are finished.</returns>
		public async UniTask CompleteAsync()
		{
			// Early exit if no jobs to process
			if (jobHandles.Length == 0) return;
			
			byte batchCount = 0;
			
			// Process from the end to make removal safer
			for (var i = jobHandles.Length - 1; i >= 0; i--)
			{
				// Only process jobs that are already completed
				if (!jobHandles[i].IsCompleted) continue;

				try
				{
					// Complete the job
					jobHandles[i].Complete();
                    
					// Invoke job completed callback
					OnJobCompleted?.Invoke();
                    
					// Remove the completed job
					jobHandles.RemoveAtSwapBack(i);
                    
				}
				catch (Exception e)
				{
					// Invoke error callback if available
					if (OnJobError != null)
					{
						OnJobError.Invoke(, e);
					}
					else
					{
						Debug.LogError($"Error completing job : {e}");
					}
                    
					// Remove the failed job
					jobHandles.RemoveAtSwapBack(i);
				}

				// Yield after processing a batch to prevent blocking
				if (batchCount < BatchSize) continue;
				OnBatchCompleted?.Invoke(jobHandles.Length);
				batchCount = 0;
				
				// If all jobs completed, trigger the callback
				if (jobHandles.Length == 0)
				{
					OnAllJobsCompleted?.Invoke();
				}
				await UniTask.Yield();
			}
		}

		/// <summary>
		///     Completes all tracked jobs without yielding.
		///     Use this when immediate completion is required.
		/// </summary>
		public void CompleteImmediate()
		{
			try
			{
				for (var i = 0; i < jobHandles.Length; i++)
				{
					try
					{
						jobHandles[i].Complete();
						OnJobCompleted?.Invoke();
					}
					catch (Exception e)
					{
						OnJobError?.Invoke(e);
					}
				}
			}
			catch (Exception e)
			{
				Debug.LogWarning("Error completing jobs " + e);
				throw;
			}
		}

		/// <summary>
		///     Returns the number of tracked job handles.
		/// </summary>
		/// <returns>The count of job handles currently being tracked.</returns>
		public int JobHandlesCount => jobHandles.Length;

		/// <summary>
		///     Checks if all scheduled jobs have been completed.
		/// </summary>
		/// <returns>
		///     <c>true</c> if all jobs are completed; otherwise, <c>false</c> if any job is still running.
		/// </returns>
		public bool AreJobsCompleted
		{
			get
			{
				foreach (JobHandle handle in jobHandles)
				{
					if (!handle.IsCompleted)
					{
						return false;
					}
				}

				return true;
			}
		}

		/// <summary>
		///     Completes all jobs and releases resources.
		/// </summary>
		public void Dispose()
		{
			CompleteImmediate();
			jobHandles.Dispose();
		}
	}
}