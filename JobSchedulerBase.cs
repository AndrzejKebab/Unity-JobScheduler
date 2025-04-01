using System;
using Cysharp.Threading.Tasks;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using ZLinq;

namespace PatataGames.JobScheduler
{
	/// <summary>
	///     Base job scheduler that provides common functionality for managing Unity job handles.
	///     Handles batched completion of jobs with yielding to prevent main thread blocking.
	/// </summary>
	[BurstCompile]
	public struct JobSchedulerBase : IDisposable
	{
		private NativeList<JobHandle> jobHandles;
		private byte                  batchSize;

		public JobSchedulerBase(int capacity = 64, byte batchSize = 8)
		{
			jobHandles     = new NativeList<JobHandle>(capacity, Allocator.Persistent);
			this.batchSize = batchSize;
		}

		/// <summary>
		///     Controls how many jobs are processed before yielding back to the main thread.
		///     Default is 8.
		/// </summary>
		public byte BatchSize
		{
			get => batchSize;
			set => batchSize = value <= 0 ? (byte)8 : value;
		}

		/// <summary>
		///     Adds a job handle to the tracking list.
		/// </summary>
		/// <param name="handle">The job handle to track.</param>
		public void ScheduleJob(JobHandle handle)
		{
			jobHandles.Add(handle);
		}

		/// <summary>
		///     Completes all tracked jobs in batches, yielding between batches to prevent
		///     blocking the main thread for too long.
		/// </summary>
		/// <returns>A UniTask that completes when all jobs are finished.</returns>
		[BurstCompile]
		public async UniTask Complete()
		{
			byte count     = 0;
			var  completed = new NativeList<int>(Allocator.Persistent);

			for (var i = 0; i < jobHandles.Length; i++)
			{
				count++;
				jobHandles[i].Complete();
				completed.Add(i);

				if (count < BatchSize) continue;
				await UniTask.Yield();
				count = 0;
			}

			for (var i = completed.Length - 1; i >= 0; i--) jobHandles.RemoveAt(completed[i]);

			completed.Dispose();
		}

		/// <summary>
		///     Completes all tracked jobs without yielding.
		///     Use this when immediate completion is required.
		/// </summary>
		[BurstCompile]
		public void CompleteAll()
		{
			for (var i = 0; i < jobHandles.Length; i++) jobHandles[i].Complete();
			jobHandles.Clear();
		}

		/// <summary>
		///     Returns the number of tracked job handles.
		/// </summary>
		/// <returns>The count of job handles currently being tracked.</returns>
		public int JobHandlesCount => jobHandles.Length;

		/// <summary>
		///     Checks if all scheduled jobs have been completed.
		/// </summary>
		/// <value>
		///     <c>true</c> if all jobs are completed; otherwise, <c>false</c> if any job is still running.
		/// </value>
		public bool AreJobsCompleted => jobHandles.AsValueEnumerable().All(handle => handle.IsCompleted);

		/// <summary>
		///     Completes all jobs and releases resources.
		/// </summary>
		public void Dispose()
		{
			CompleteAll();
			jobHandles.Dispose();
		}
	}
}