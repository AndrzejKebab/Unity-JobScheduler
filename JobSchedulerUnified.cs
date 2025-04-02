using System;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using ZLinq;

namespace PatataGames.JobScheduler
{
	public interface IJobData
	{
		public JobHandle Schedule();
	}

    /// <summary>
    ///     Unified job scheduler that works with different job types through a common interface.
    ///     Supports both immediate scheduling and storing jobs for later scheduling.
    /// </summary>
    [BurstCompile]
    public struct JobSchedulerUnified : IDisposable
	{
		private JobSchedulerBase   jobSchedulerBase;
		private NativeList<IntPtr> jobPtrs; // Using IntPtr to store GCHandle values for job datas

		public JobSchedulerUnified(int initialCapacity = 64, byte batchSize = 8)
		{
			jobSchedulerBase = new JobSchedulerBase(initialCapacity, batchSize);
			jobPtrs          = new NativeList<IntPtr>(initialCapacity, Allocator.Persistent);
		}

		#region Add Job methods
        /// <summary>
        ///     Add an IJob to the scheduler without scheduling it
        /// </summary>
        [BurstCompile]
        public int AddJob<T>(T job) where T : unmanaged, IJob
		{
			try
			{
				var      jobData = new JobData<T> { Job = job };
				GCHandle handle  = GCHandle.Alloc(jobData, GCHandleType.Pinned);
				jobPtrs.Add((IntPtr)handle);
				return jobPtrs.Length - 1; // Return index of added job
			}
			catch (Exception e)
			{
				Debug.LogWarning($"Failed to add job of type {typeof(T).Name}: " + e);
				throw;
			}
		}

        /// <summary>
        ///     Add an IJobFor to the scheduler without scheduling it
        /// </summary>
        [BurstCompile]
        public int AddJob<T>(T job, int arrayLength, JobHandle dependency = default)
			where T : unmanaged, IJobFor
		{
			try
			{
				if (arrayLength <= 0)
				{
					throw new ArgumentOutOfRangeException(nameof(arrayLength), "Array length must be positive");
				}
                
				var jobData = new JobForData<T>
				              {
					              Job         = job,
					              ArrayLength = arrayLength,
					              Dependency  = dependency
				              };
				GCHandle handle = GCHandle.Alloc(jobData, GCHandleType.Pinned);
				jobPtrs.Add((IntPtr)handle);
				return jobPtrs.Length - 1; // Return index of added job
			}
			catch (Exception e)
			{
				Debug.LogWarning($"Failed to add job of type {typeof(T).Name}: " + e);
				throw;
			}
		}

        /// <summary>
        ///     Add an IJobParallelFor to the scheduler without scheduling it
        /// </summary>
        [BurstCompile]
        public int AddJobParallel<T>(T job, int arrayLength, int innerLoopBatchCount = 0)
			where T : unmanaged, IJobParallelFor
		{
			try
			{
				if (arrayLength <= 0)
				{
					throw new ArgumentOutOfRangeException(nameof(arrayLength), "Array length must be positive");
				}

				if (innerLoopBatchCount <= 0) innerLoopBatchCount = jobSchedulerBase.BatchSize;


				var jobData = new JobParallelForData<T>
				              {
					              Job            = job,
					              ArrayLength    = arrayLength,
					              InnerBatchSize = innerLoopBatchCount
				              };
				GCHandle handle = GCHandle.Alloc(jobData, GCHandleType.Pinned);
				jobPtrs.Add((IntPtr)handle);
				return jobPtrs.Length - 1; // Return index of added job
			}
			catch (Exception e)
			{
				Debug.LogWarning($"Failed to add job of type {typeof(T).Name}: " + e);
				throw;
			}
		}
		#endregion

		#region Schedule Stored Jobs
        /// <summary>
        ///     Schedule a specific stored job by index
        /// </summary>
        [BurstCompile]
        public JobHandle ScheduleJob(int jobIndex)
		{
			if (jobIndex < 0 || jobIndex >= jobPtrs.Length)
				throw new ArgumentOutOfRangeException(nameof(jobIndex), "Job index is out of range");

			IntPtr ptr    = jobPtrs[jobIndex];
			GCHandle    handle = GCHandle.FromIntPtr(ptr);

			if (!handle.IsAllocated)
				throw new InvalidOperationException($"Job at index {jobIndex} is not allocated and has been freed");

			// Handle different job data types
			var target = handle.Target;
			if (target is not IJobData jobData)
				throw new InvalidOperationException($"Invalid job data type: {target?.GetType().Name ?? "null"}");
			JobHandle jobHandle = jobData.Schedule();
			jobSchedulerBase.AddJobHandle(jobHandle);
			return jobHandle;

		}

        /// <summary>
        ///     Schedule all stored jobs
        /// </summary>
        [BurstCompile]
        public void ScheduleAll()
		{
			for (var i = 0; i < jobPtrs.Length; i++)
			{
				IntPtr ptr    = jobPtrs[i];
				GCHandle    handle = GCHandle.FromIntPtr(ptr);

				if (!handle.IsAllocated) 
				{
					Debug.LogWarning($"Job at index {i} is not allocated, skipping");
					continue;
				}
				var target = handle.Target;
				if (target is IJobData jobData)
				{
					try
					{
						JobHandle jobHandle = jobData.Schedule();
						jobSchedulerBase.AddJobHandle(jobHandle);
					}
					catch (Exception e)
					{
						Debug.LogWarning($"Failed to schedule job at index {i}: " + e);
						throw;
					}
				}
				else
				{
					Debug.LogWarning($"Item at index {i} is not a valid job data: {target?.GetType().Name ?? "null"}");
				}
			}
		}

        /// <summary>
        ///     Clear all stored jobs without scheduling them
        /// </summary>
        [BurstCompile]
        public void ClearStoredJobs()
        {
	        foreach (GCHandle handle in jobPtrs.AsValueEnumerable().Cast<GCHandle>().Where(handle => handle.IsAllocated))
	        {
		        handle.Free();
	        }

	        jobPtrs.Clear();
        }
		#endregion

		#region Schedule Job methods (immediate scheduling)
        /// <summary>
        ///     Schedule an IJob immediately
        /// </summary>
        [BurstCompile]
        public void Schedule<T>(T job, JobHandle dependsOn = default) where T : struct, IJob
		{
			try
			{
				JobHandle handle = job.Schedule(dependsOn);
				jobSchedulerBase.AddJobHandle(handle);
			}
			catch (Exception e)
			{
				Debug.LogWarning($"Failed to schedule IJob of type {typeof(T).Name}: " + e);
				throw;
			}
		}

        /// <summary>
        ///     Schedule an IJobFor immediately
        /// </summary>
        [BurstCompile]
        public void Schedule<T>(T job, int arrayLength, JobHandle dependsOn = default)
			where T : struct, IJobFor
		{
			try
			{
				JobHandle handle = job.Schedule(arrayLength, dependsOn);
				jobSchedulerBase.AddJobHandle(handle);
			}
			catch (Exception e)
			{
				Debug.LogWarning($"Failed to schedule IJobFor of type {typeof(T).Name} with array length {arrayLength}: " + e);
				throw;
			}
		}

        /// <summary>
        ///     Schedule an IJobParallelFor immediately
        /// </summary>
        [BurstCompile]
        public void ScheduleParallel<T>(T         job, int arrayLength, int innerLoopBatchCount = 0,
                                             JobHandle dependsOn = default) where T : struct, IJobParallelFor
		{
			if (arrayLength <= 0)
			{
				Debug.LogWarning($"Invalid array length {arrayLength} for IJobParallelFor of type {typeof(T).Name}");
				throw new ArgumentOutOfRangeException(nameof(arrayLength), "Array length must be positive");
			}
            
			if (innerLoopBatchCount <= 0) innerLoopBatchCount = jobSchedulerBase.BatchSize;

			try
			{
				JobHandle handle = job.Schedule(arrayLength, innerLoopBatchCount, dependsOn);
				jobSchedulerBase.AddJobHandle(handle);
			}
			catch (Exception e)
			{
				Debug.LogWarning($"Failed to schedule IJobParallelFor of type {typeof(T).Name} with array length {arrayLength}: " + e);
				throw;
			}
		}
		#endregion

        /// <summary>
        ///     Complete all scheduled jobs
        /// </summary>
        [BurstCompile]
        public void CompleteAll()
		{
			jobSchedulerBase.CompleteAll();
		}
		
		[BurstCompile]
		public void Dispose()
		{
			// Free all GCHandles
			if (jobPtrs.IsCreated)
			{
				foreach (IntPtr ptr in jobPtrs)
				{
					if (ptr == IntPtr.Zero) continue;
					GCHandle handle = GCHandle.FromIntPtr(ptr);
					if (handle.IsAllocated)
					{
						handle.Free();
					}
				}

				// Dispose the native list
				jobPtrs.Dispose();
			}

			// Dispose native containers
			jobSchedulerBase.Dispose();
		}
	}
}