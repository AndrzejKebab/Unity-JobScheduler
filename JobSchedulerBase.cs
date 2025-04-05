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
		///     Initializes a new instance of the JobSchedulerBase struct.
		/// </summary>
		/// <param name="capacity">Initial capacity for the job handles list. Default is 64.</param>
		/// <param name="batchSize">Number of jobs to process before yielding. Default is 32.</param>
		public JobSchedulerBase(int capacity = 64, byte batchSize = 32)
		{
			jobHandles     = new NativeList<JobHandle>(capacity, Allocator.Persistent);
			this.batchSize = batchSize;
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

				// Complete the job and remove it
				jobHandles[i].Complete();
				jobHandles.RemoveAtSwapBack(i);

				// Increment batch counter
				batchCount++;

				// Yield after processing a batch to prevent blocking
				if (batchCount < BatchSize) continue;
				await UniTask.Yield();
				batchCount = 0;
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
				for (var i = 0; i < jobHandles.Length; i++) jobHandles[i].Complete();
				jobHandles.Clear();
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