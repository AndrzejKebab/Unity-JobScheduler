using System;
using Cysharp.Threading.Tasks;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace PatataGames.JobScheduler;

/// <summary>
///     Base job scheduler that provides common functionality for managing Unity job handles.
///     Handles batched completion of jobs with yielding to prevent main thread blocking.
/// </summary>
public struct JobSchedulerBase : IDisposable
{
	// Store job handles in a native collection for performance and Burst compatibility
	private NativeHashMap<Guid, JobHandle> jobHandles;
	private byte                           batchSize;

	/// <summary>
	///     Delegate for job completion without parameters.
	/// </summary>
	public delegate void JobCompleteCallback(JobHandle handle);

	public delegate void JobErrorCallback(Exception e);

	/// <summary>
	///     Delegate for batch completion callbacks.
	/// </summary>
	/// <param name="remainingCount">Number of jobs remaining after this batch.</param>
	public delegate void BatchCompletedCallback(int remainingCount);

	/// <summary>
	///     Invoked when a specific job completes successfully.
	/// </summary>
	public JobCompleteCallback OnJobCompleted;

	/// <summary>
	///     Invoked when a batch of jobs completes.
	/// </summary>
	public BatchCompletedCallback OnBatchCompleted;

	/// <summary>
	///     Invoked when all scheduled jobs have completed.
	/// </summary>
	public Action OnAllJobsCompleted;

	public JobErrorCallback OnJobErrorCallback;

	/// <summary>
	///     Initializes a new instance of the JobSchedulerBase struct.
	/// </summary>
	/// <param name="capacity">Initial capacity for the job handles list. Default is 64.</param>
	/// <param name="batchSize">Number of jobs to process before yielding. Default is 32.</param>
	public JobSchedulerBase(int capacity = 64, byte batchSize = 32)
	{
		jobHandles     = new NativeHashMap<Guid, JobHandle>(capacity, Allocator.Persistent);
		this.batchSize = batchSize;

		// Initialize callbacks as null
		OnJobCompleted     = null;
		OnBatchCompleted   = null;
		OnAllJobsCompleted = null;
		OnJobErrorCallback = null;
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
	///     Adds a job handle to the tracking list without a specific callback.
	/// </summary>
	/// <param name="handle">The job handle to track.</param>
	public void AddJobHandle(JobHandle handle, Guid jobId)
	{
		jobHandles.Add(jobId, handle);
	}


	/// <summary>
	///     Completes all tracked jobs in batches, yielding between batches to prevent
	///     blocking the main thread for too long.
	/// </summary>
	/// <returns>A UniTask that completes when all jobs are finished.</returns>
	public async UniTask CompleteAsync()
	{
		try
		{
			// Early exit if no jobs to process or already disposed
			if (!jobHandles.IsCreated || jobHandles.Count == 0) return;

			// Take a snapshot of the keys to avoid modification during iteration
			NativeArray<Guid> jobKeys = jobHandles.GetKeyArray(Allocator.Temp);

			byte batchCount = 0;
			for (var i = 0; i < jobKeys.Length; i++)
			{
				// Check if we've been disposed during iteration
				if (!jobHandles.IsCreated)
				{
					jobKeys.Dispose();
					return;
				}

				Guid jobKey = jobKeys[i];

				// Skip if key no longer exists
				if (!jobHandles.TryGetValue(jobKey, out JobHandle jobHandle))
					continue;

				// Only process jobs that are already completed
				if (!jobHandle.IsCompleted)
					continue;

				try
				{
					// Invoke the global job completed callback
					OnJobCompleted?.Invoke(jobHandle);

					// Complete the job
					jobHandle.Complete();

					// Remove the completed job
					if (jobHandles.IsCreated)
						jobHandles.Remove(jobKey);
				}
				catch (Exception e)
				{
					Debug.LogError($"Error completing job: {e}");
					OnJobErrorCallback?.Invoke(e);

					// Remove the failed job
					if (jobHandles.IsCreated) jobHandles.Remove(jobKey);
				}
				
				// Increment batch counter
				batchCount++;

				// Check if we've completed a batch
				if (batchCount < BatchSize) continue;

				// Invoke the batch completed event
				OnBatchCompleted?.Invoke(jobHandles.Count);

				jobKeys.Dispose(); // Dispose before yielding
				await UniTask.Yield();

				// Re-check if we've been disposed after the yield
				if (!jobHandles.IsCreated)
					return;

				// Get a fresh key array after yielding
				jobKeys    = jobHandles.GetKeyArray(Allocator.Temp);
				batchCount = 0;
			}

			// Clean up
			jobKeys.Dispose();

			// Invoke batch completed one final time if we processed any jobs in the last incomplete batch
			if (batchCount > 0) OnBatchCompleted?.Invoke(jobHandles.Count);

			// If all jobs completed, trigger the callback
			if (jobHandles.IsCreated && jobHandles.Count == 0)
				OnAllJobsCompleted?.Invoke();
		}
		catch (Exception e)
		{
			Debug.LogError($"Exception in CompleteAsync: {e}");
			OnJobErrorCallback?.Invoke(e);
		}
	}

	/// <summary>
	///     Completes all tracked jobs without yielding.
	///     Use this when immediate completion is required.
	/// </summary>
	public void CompleteImmediate()
	{
		if (!jobHandles.IsCreated) return;

		try
		{
			NativeArray<Guid> jobKeys = jobHandles.GetKeyArray(Allocator.Temp);

			foreach (Guid jobKey in jobKeys)
				if (jobHandles.TryGetValue(jobKey, out JobHandle jobHandle))
				{
					jobHandle.Complete();
					// Invoke the global job completed callback
					OnJobCompleted?.Invoke(jobHandle);
				}

			jobKeys.Dispose();

			// Clear all jobs and callbacks
			jobHandles.Clear();

			// If we completed any jobs, trigger the all completed callback
			OnAllJobsCompleted?.Invoke();
		}
		catch (Exception e)
		{
			OnJobErrorCallback?.Invoke(e);
			Debug.LogWarning("Error completing jobs " + e);
			throw;
		}
	}

	/// <summary>
	///     Returns the number of tracked job handles.
	/// </summary>
	/// <returns>The count of job handles currently being tracked.</returns>
	public int JobHandlesCount => jobHandles.Count;

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
			foreach (KVPair<Guid, JobHandle> Job in jobHandles)
			{
				jobHandles.TryGetValue(Job.Key, out JobHandle jobHandle);
				if (!jobHandle.IsCompleted) return false;
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