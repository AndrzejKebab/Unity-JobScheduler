using System;
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
			var      jobData = new JobData<T> { Job = job };
			GCHandle handle  = GCHandle.Alloc(jobData, GCHandleType.Pinned);
			jobPtrs.Add((IntPtr)handle);
			return jobPtrs.Length - 1; // Return index of added job
		}

        /// <summary>
        ///     Add an IJobFor to the scheduler without scheduling it
        /// </summary>
        [BurstCompile]
        public int AddJob<T>(T job, int arrayLength, JobHandle dependency = default)
			where T : unmanaged, IJobFor
		{
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

        /// <summary>
        ///     Add an IJobParallelFor to the scheduler without scheduling it
        /// </summary>
        [BurstCompile]
        public int AddJobParallel<T>(T job, int arrayLength, int innerLoopBatchCount = 0)
			where T : unmanaged, IJobParallelFor
		{
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
			var    handle = (GCHandle)ptr;

			if (!handle.IsAllocated)
				throw new InvalidOperationException("The job handle has been freed");

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
				var    handle = (GCHandle)ptr;

				if (!handle.IsAllocated) continue;
				var target = handle.Target;
				if (target is IJobData jobData)
				{
					JobHandle jobHandle = jobData.Schedule();
					jobSchedulerBase.AddJobHandle(jobHandle);
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
        public JobHandle Schedule<T>(T job, JobHandle dependsOn = default) where T : struct, IJob
		{
			JobHandle handle = job.Schedule(dependsOn);
			jobSchedulerBase.AddJobHandle(handle);
			return handle;
		}

        /// <summary>
        ///     Schedule an IJobFor immediately
        /// </summary>
        [BurstCompile]
        public JobHandle Schedule<T>(T job, int arrayLength, JobHandle dependsOn = default)
			where T : struct, IJobFor
		{
			JobHandle handle = job.Schedule(arrayLength, dependsOn);
			jobSchedulerBase.AddJobHandle(handle);
			return handle;
		}

        /// <summary>
        ///     Schedule an IJobParallelFor immediately
        /// </summary>
        [BurstCompile]
        public JobHandle ScheduleParallel<T>(T         job, int arrayLength, int innerLoopBatchCount = 0,
                                             JobHandle dependsOn = default) where T : struct, IJobParallelFor
		{
			if (innerLoopBatchCount <= 0) innerLoopBatchCount = jobSchedulerBase.BatchSize;

			JobHandle handle = job.Schedule(arrayLength, innerLoopBatchCount, dependsOn);
			jobSchedulerBase.AddJobHandle(handle);
			return handle;
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
			foreach (GCHandle handle in jobPtrs.AsValueEnumerable().Cast<GCHandle>().Where(handle => handle.IsAllocated))
			{
				handle.Free();
			}

			// Dispose native containers
			jobSchedulerBase.Dispose();
			if (jobPtrs.IsCreated) jobPtrs.Dispose();
		}
	}

	#region Test Example
	// Example job implementations
	[BurstCompile]
	public struct TestJob : IJob
	{
		public void Execute()
		{
			// Implementation
		}
	}

	[BurstCompile]
	public struct TestJobFor : IJobFor
	{
		public void Execute(int index)
		{
			// Implementation
		}
	}

	[BurstCompile]
	public struct TestJobParallelFor : IJobParallelFor
	{
		public void Execute(int index)
		{
			// Implementation
		}
	}
	
		
	/// <summary>
	///     Example usage of JobSchedulerUnified
	/// </summary>
	public class JobSchedulerExample : MonoBehaviour
	{
		private JobSchedulerUnified scheduler;

		private void Start()
		{
			scheduler = new JobSchedulerUnified(32, 4);

			// Example of adding jobs without scheduling them immediately
			var testJob1 = new TestJob();
			var testJob2 = new TestJobFor();
			var testJob3 = new TestJobParallelFor();

			var job1Index = scheduler.AddJob(testJob1);
			var job2Index = scheduler.AddJob(testJob2, 100);
			var job3Index = scheduler.AddJobParallel(testJob3, 1000, 64);

			// Later in your code, you can schedule these jobs
			scheduler.ScheduleJob(job1Index);
			// Or schedule all at once
			scheduler.ScheduleAll();
		}

		private void Update()
		{
			// Example usage with different job types (immediate scheduling)
			var testJob         = new TestJob();
			var testJobFor      = new TestJobFor();
			var testParallelJob = new TestJobParallelFor();

			// Schedule jobs immediately
			scheduler.Schedule(testJob);
			scheduler.Schedule(testJobFor, 100);
			scheduler.ScheduleParallel(testParallelJob, 1000, 64);

			// Wait for all jobs to complete
			scheduler.CompleteAll();
		}

		private void OnDestroy()
		{
			// Ensure proper cleanup
			scheduler.Dispose();
		}
	}
	#endregion
}