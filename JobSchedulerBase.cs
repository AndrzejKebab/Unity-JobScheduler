
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Cysharp.Threading.Tasks;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
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
        // Delegates for job completion callbacks
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void BurstCallback();

        // Structure to hold job handle and its associated callback
        private struct JobHandleWithCallback
        {
            public JobHandle Handle;
            public FunctionPointer<BurstCallback> CallbackPtr;
            public int CallbackId; // ID for non-burst callbacks

            public JobHandleWithCallback(JobHandle handle)
            {
                Handle = handle;
                CallbackPtr = default;
                CallbackId = -1;
            }

            public JobHandleWithCallback(JobHandle handle, FunctionPointer<BurstCallback> callbackPtr)
            {
                Handle = handle;
                CallbackPtr = callbackPtr;
                CallbackId = -1;
            }

            public JobHandleWithCallback(JobHandle handle, int callbackId)
            {
                Handle = handle;
                CallbackPtr = default;
                CallbackId = callbackId;
            }
        }

        private NativeList<JobHandleWithCallback> jobHandles;
        private byte batchSize;
        private int globalCallbackId;
        private bool isProcessingJobs; // Lock to prevent concurrent modifications

        // Static callback registry for non-burst callbacks (must be initialized at startup)
        private static CallbackRegistry callbackRegistry;

        // Make sure to call this before using JobSchedulerBase
        public static void InitializeCallbackSystem()
        {
            callbackRegistry ??= new CallbackRegistry();
        }

        /// <summary>
        ///     Initializes a new instance of the JobSchedulerBase struct.
        /// </summary>
        /// <param name="capacity">Initial capacity for the job handles list. Default is 64.</param>
        /// <param name="batchSize">Number of jobs to process before yielding. Default is 32.</param>
        public JobSchedulerBase(int capacity = 64, byte batchSize = 32)
        {
            if (callbackRegistry == null)
            {
                Debug.LogWarning("CallbackRegistry not initialized. Call JobSchedulerBase.InitializeCallbackSystem() first.");
                callbackRegistry = new CallbackRegistry();
            }

            jobHandles = new NativeList<JobHandleWithCallback>(capacity, Allocator.Persistent);
            this.batchSize = batchSize;
            globalCallbackId = -1;
            isProcessingJobs = false;
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
        ///     Register a global callback that will be called when all jobs are completed.
        /// </summary>
        /// <param name="callback">The callback to register.</param>
        public void RegisterGlobalCallback(Action callback)
        {
            globalCallbackId = callbackRegistry.RegisterCallback(callback);
        }

        /// <summary>
        ///     Adds a job handle to the tracking list.
        /// </summary>
        /// <param name="handle">The job handle to track.</param>
        public void AddJobHandle(JobHandle handle)
        {
            // Don't add new jobs while processing existing ones
            if (isProcessingJobs)
            {
                Debug.LogWarning("Attempting to add job handle while processing jobs. This operation will be ignored.");
                return;
            }
            
            jobHandles.Add(new JobHandleWithCallback(handle));
        }

        /// <summary>
        ///     Adds a job handle with a Burst-compatible static callback.
        /// </summary>
        /// <param name="handle">The job handle to track.</param>
        /// <param name="burstCallback">Static callback function compatible with Burst.</param>
        public void AddJobHandle(JobHandle handle, FunctionPointer<BurstCallback> burstCallback)
        {
            // Don't add new jobs while processing existing ones
            if (isProcessingJobs)
            {
                Debug.LogWarning("Attempting to add job handle while processing jobs. This operation will be ignored.");
                return;
            }
            
            if (burstCallback.IsCreated)
            {
                var functionPtr = burstCallback;
                jobHandles.Add(new JobHandleWithCallback(handle, functionPtr));
            }
            else
            {
                jobHandles.Add(new JobHandleWithCallback(handle));
            }
        }

        /// <summary>
        ///     Adds a job handle with a managed (non-Burst) callback. These will run on the main thread.
        /// </summary>
        /// <param name="handle">The job handle to track.</param>
        /// <param name="callback">Managed callback that will be invoked on the main thread.</param>
        public void AddJobHandleWithManagedCallback(JobHandle handle, Action callback)
        {
            // Don't add new jobs while processing existing ones
            if (isProcessingJobs)
            {
                Debug.LogWarning("Attempting to add job handle while processing jobs. This operation will be ignored.");
                return;
            }
            
            int callbackId = callbackRegistry.RegisterCallback(callback);
            jobHandles.Add(new JobHandleWithCallback(handle, callbackId));
        }

        /// <summary>
        ///     Completes all tracked jobs in batches, yielding between batches to prevent
        ///     blocking the main thread for too long. Invokes callbacks for completed jobs.
        /// </summary>
        /// <returns>A UniTask that completes when all jobs are finished.</returns>
        public async UniTask CompleteAsync()
        {
            // Set processing flag to prevent concurrent modification
            if (isProcessingJobs)
            {
                // Already processing, just wait for the current process to finish
                await UniTask.Yield();
                return;
            }
            
            isProcessingJobs = true;
            
            try
            {
                // Early exit if no jobs to process
                if (jobHandles.Length == 0)
                {
                    if (globalCallbackId >= 0)
                    {
                        callbackRegistry.InvokeCallback(globalCallbackId);
                    }
                    return;
                }
                
                byte batchCount = 0;
                var completedIndices = new List<int>(jobHandles.Length); // Track indices to remove
                
                // First pass: identify completed jobs
                for (var i = 0; i < jobHandles.Length; i++)
                {
                    if (!jobHandles[i].Handle.IsCompleted) continue;
                    
                    completedIndices.Add(i);
                    batchCount++;
                    
                    if (batchCount >= BatchSize)
                    {
                        await UniTask.Yield();
                        batchCount = 0;
                    }
                }
                
                // Second pass: process completed jobs in reverse order (to make removal safer)
                completedIndices.Sort((a, b) => b.CompareTo(a)); // Sort in descending order
                
                foreach (int i in completedIndices)
                {
                    if (i >= jobHandles.Length)
                    {
                        Debug.LogError($"Index {i} is out of range in jobHandles of length {jobHandles.Length}. Skipping this job.");
                        continue;
                    }
                    
                    // Complete the job
                    jobHandles[i].Handle.Complete();
                    
                    // Invoke burst callback if exists
                    if (jobHandles[i].CallbackPtr.IsCreated)
                    {
                        try
                        {
                            jobHandles[i].CallbackPtr.Invoke();
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"Error in Burst job completion callback: {e}");
                        }
                    }
                    
                    // Invoke managed callback if exists
                    if (jobHandles[i].CallbackId >= 0)
                    {
                        try
                        {
                            callbackRegistry.InvokeCallback(jobHandles[i].CallbackId);
                            callbackRegistry.UnregisterCallback(jobHandles[i].CallbackId);
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"Error in managed job completion callback: {e}");
                        }
                    }
                    
                    // Remove the job safely
                    if (i < jobHandles.Length) // Double-check to avoid index errors
                    {
                        jobHandles.RemoveAtSwapBack(i);
                    }
                }
                
                // If all jobs are now completed, invoke the global callback
                if (jobHandles.Length == 0 && globalCallbackId >= 0)
                {
                    callbackRegistry.InvokeCallback(globalCallbackId);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error in CompleteAsync: {e}");
            }
            finally
            {
                // Reset processing flag
                isProcessingJobs = false;
            }
        }

        /// <summary>
        ///     Completes all tracked jobs without yielding and invokes callbacks.
        ///     Use this when immediate completion is required.
        /// </summary>
        public void CompleteImmediate()
        {
            // Set processing flag to prevent concurrent modification
            isProcessingJobs = true;
            
            try
            {
                var jobCount = jobHandles.Length;
                
                for (var i = 0; i < jobCount; i++)
                {
                    // Check for valid index
                    if (i >= jobHandles.Length)
                    {
                        Debug.LogWarning($"Index {i} is out of range in jobHandles array of length {jobHandles.Length}");
                        break;
                    }
                    
                    jobHandles[i].Handle.Complete();
                    
                    // Invoke burst callback if exists
                    if (jobHandles[i].CallbackPtr.IsCreated)
                    {
                        try
                        {
                            jobHandles[i].CallbackPtr.Invoke();
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"Error in Burst job completion callback: {e}");
                        }
                    }
                    
                    // Invoke managed callback if exists
                    if (jobHandles[i].CallbackId >= 0)
                    {
                        try
                        {
                            callbackRegistry.InvokeCallback(jobHandles[i].CallbackId);
                            callbackRegistry.UnregisterCallback(jobHandles[i].CallbackId);
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"Error in managed job completion callback: {e}");
                        }
                    }
                }
                
                // Clear all handles after processing
                jobHandles.Clear();
                
                // Invoke the global callback
                if (globalCallbackId >= 0)
                {
                    callbackRegistry.InvokeCallback(globalCallbackId);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Error completing jobs: {e}");
                throw;
            }
            finally
            {
                // Reset processing flag
                isProcessingJobs = false;
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
            
            if (jobHandles.IsCreated)
            {
                jobHandles.Dispose();
            }
            
            // Unregister the global callback if it exists
            if (globalCallbackId >= 0)
            {
                callbackRegistry.UnregisterCallback(globalCallbackId);
                globalCallbackId = -1;
            }
        }
    }

    /// <summary>
    /// A registry for managing managed callbacks that can't be directly used with Burst.
    /// </summary>
    public class CallbackRegistry
    {
        private readonly Dictionary<int, Action> callbacks = new Dictionary<int, Action>();
        private          int                     nextId    = 0;

        public int RegisterCallback(Action callback)
        {
            if (callback == null) return -1;
            
            int id = nextId++;
            callbacks[id] = callback;
            return id;
        }

        public void UnregisterCallback(int id)
        {
            if (callbacks.ContainsKey(id))
            {
                callbacks.Remove(id);
            }
        }

        public void InvokeCallback(int id)
        {
            if (callbacks.TryGetValue(id, out Action callback))
            {
                callback?.Invoke();
            }
        }
    }
}