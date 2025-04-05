
using System;
using Cysharp.Threading.Tasks;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

namespace PatataGames.JobScheduler
{
    /// <summary>
    ///     Base job scheduler that provides common functionality for managing Unity job handles.
    ///     Handles batched completion of jobs with yielding to prevent main thread blocking.
    ///     Supports callbacks for job completion events.
    /// </summary>
    public struct JobSchedulerBase : IDisposable
    {
        // Structure to hold job handle and its associated callback
        private struct JobHandleWithCallback
        {
            public JobHandle                           Handle;
            public FunctionPointer<OnAllJobsCompleted> OnCompleted;

            public JobHandleWithCallback(JobHandle handle, OnAllJobsCompleted onCompleted = null)
            {
                Handle      = handle;
                OnCompleted = BurstCompiler.CompileFunctionPointer(onCompleted);
            }
        }

        private NativeList<JobHandleWithCallback> jobHandles;
        private byte batchSize;

        // Event raised when all jobs are completed
        public delegate void               OnAllJobsCompleted();

        private OnAllJobsCompleted allJobsCompleted;

        /// <summary>
        ///     Initializes a new instance of the JobSchedulerBase struct.
        /// </summary>
        /// <param name="capacity">Initial capacity for the job handles list. Default is 64.</param>
        /// <param name="batchSize">Number of jobs to process before yielding. Default is 32.</param>
        public JobSchedulerBase(int capacity = 64, byte batchSize = 32)
        {
            jobHandles       = new NativeList<JobHandleWithCallback>(capacity, Allocator.Persistent);
            this.batchSize   = batchSize;
            allJobsCompleted = null;
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
            jobHandles.Add(new JobHandleWithCallback(handle));
        }

        /// <summary>
        ///     Adds a job handle to the tracking list with a callback that will be invoked when the job completes.
        /// </summary>
        /// <param name="handle">The job handle to track.</param>
        /// <param name="onCompleted">Callback action that will be invoked when the job completes.</param>
        public void AddJobHandle(JobHandle handle, OnAllJobsCompleted onCompleted)
        {
            jobHandles.Add(new JobHandleWithCallback(handle, onCompleted));
        }

        /// <summary>
        ///     Completes all tracked jobs in batches, yielding between batches to prevent
        ///     blocking the main thread for too long. Invokes callbacks for completed jobs.
        /// </summary>
        /// <returns>A UniTask that completes when all jobs are finished.</returns>
        public async UniTask CompleteAsync()
        {
            // Early exit if no jobs to process
            if (jobHandles.Length == 0)
            {
                allJobsCompleted?.Invoke();
                return;
            }
            
            byte batchCount = 0;
            
            // Process from the end to make removal safer
            for (var i = jobHandles.Length - 1; i >= 0; i--)
            {
                // Only process jobs that are already completed
                if (!jobHandles[i].Handle.IsCompleted) continue;

                // Complete the job
                jobHandles[i].Handle.Complete();
                
                // Invoke callback if exists
                try
                {
                    jobHandles[i].OnCompleted.Invoke();
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error in job completion callback: {e}");
                }
                
                // Remove the job
                jobHandles.RemoveAtSwapBack(i);

                // Increment batch counter
                batchCount++;

                // Yield after processing a batch to prevent blocking
                if (batchCount < BatchSize) continue;
                await UniTask.Yield();
                batchCount = 0;
            }

            // If all jobs are now completed, raise the event
            if (jobHandles.Length == 0)
            {
                allJobsCompleted?.Invoke();
            }
        }

        /// <summary>
        ///     Completes all tracked jobs without yielding and invokes callbacks.
        ///     Use this when immediate completion is required.
        /// </summary>
        public void CompleteImmediate()
        {
            try
            {
                for (var i = 0; i < jobHandles.Length; i++)
                {
                    jobHandles[i].Handle.Complete();
                    
                    // Invoke callback if exists
                    try
                    {
                        jobHandles[i].OnCompleted.Invoke();
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"Error in job completion callback: {e}");
                    }
                }
                
                jobHandles.Clear();
                
                // Raise the all-complete event
                allJobsCompleted?.Invoke();
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
                foreach (var jobData in jobHandles)
                {
                    if (!jobData.Handle.IsCompleted)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>
        ///     Completes all jobs, invokes callbacks, and releases resources.
        /// </summary>
        public void Dispose()
        {
            CompleteImmediate();
            jobHandles.Dispose();
        }
    }
}