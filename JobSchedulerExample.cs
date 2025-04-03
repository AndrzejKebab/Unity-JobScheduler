using System;
using System.Collections;
using System.Diagnostics;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using Random = UnityEngine.Random;
using Debug = UnityEngine.Debug;

namespace PatataGames.JobScheduler
{		
	#region Test Jobs
	[BurstCompile]
	public struct TestOutput : IJob
	{
		public NativeArray<float> A;
		public NativeArray<float> B;
		public void Execute()
		{
			B[0] = A[0] * 2137f;
		}
	}
	// Example job implementations
	[BurstCompile]
	public struct TestJob : IJob
	{
		public float A;
		public float B;

		public void Execute()
		{
			// Expensive calculation - simulating complex math operations
			var value = A;
			for (var i = 0; i < 10000; i++)
			{
				value = Mathf.Sin(value) * Mathf.Cos(B * i) + Mathf.Sqrt(Mathf.Abs(value * 0.5f));
				value = Mathf.Exp(Mathf.Clamp(value, -3f, 3f));
			}
		}
	}

	[BurstCompile]
	public struct TestJobFor : IJobFor
	{
		// Input data
		[ReadOnly]  public NativeArray<float>           Input;

		// Parameters
		public float Multiplier;

		public void Execute(int index)
		{
			// Expensive calculation per element - simulating data processing
			var value = Input[index];
			for (var i = 0; i < 1000; i++)
			{
				value = Mathf.Pow(value, 1.01f) + Mathf.Sin(value * Multiplier);
				value = Mathf.Log(Mathf.Max(1.0f, value)) * 0.5f;
			}
		}
	}

	[BurstCompile]
	public struct TestJobParallelFor : IJobParallelFor
	{
		// Input data
		[ReadOnly]  public NativeArray<Vector3>           Positions;

		// Parameters
		public float   DeltaTime;
		public Vector3 Force;

		public void Execute(int index)
		{
			// Expensive calculation - simulating physics or particle calculations
			Vector3 pos = Positions[index];
			Vector3 velocity = Vector3.zero;

			// Simulate a mini physics system
			for (var i = 0; i < 500; i++)
			{
				velocity += Force * DeltaTime;
				velocity *= 0.99f; // damping
				pos += velocity * DeltaTime;

				// Add some turbulence
				var noise = Mathf.Sin(pos.x * 0.1f) * Mathf.Cos(pos.y * 0.1f) * Mathf.Sin(pos.z * 0.1f + i * 0.01f);
				pos += new Vector3(noise, noise, noise) * 0.01f;
			}
		}
	}

	#endregion

	/// <summary>
	/// Simple example of JobSchedulerUnified with clean debug logs
	/// </summary>
	public class JobSchedulerExample : MonoBehaviour
	{
		private JobSchedulerUnified scheduler;

		// Native collections for job data
		private NativeArray<float> inputArray;
		private NativeArray<Vector3> positionArray;

		// Job counts
		private int regularJobCount;
		private int jobForCount;
		private int parallelJobCount;
		private int totalJobCount;

		// Timing
		private Stopwatch sw;

		private NativeArray<float>             a;
		private NativeArray<float>             b;
		
		private void Awake()
		{
			Debug.Log("[JobScheduler] Initializing scheduler");
			scheduler = new JobSchedulerUnified(300, 16);
			sw = new Stopwatch();
		}

		private void Start()
		{
			try
			{
				// Start timing
				sw.Start();
				
				// Allocate native collections
				Debug.Log("[JobScheduler] Allocating native collections");
				inputArray    = new NativeArray<float>(1000, Allocator.Persistent);
				positionArray = new NativeArray<Vector3>(1000, Allocator.Persistent);
				a             = new NativeArray<float>(1, Allocator.Persistent);
				b             = new NativeArray<float>(1, Allocator.Persistent);
				a[0]          = 2.5f;
				b[0]          = 0f;
				
				// Initialize input data
				Debug.Log("[JobScheduler] Initializing input data");
				for (var i = 0; i < inputArray.Length; i++)
				{
					inputArray[i] = Random.Range(-100, 100);
				}

				for (var i = 0; i < positionArray.Length; i++)
				{
					positionArray[i] = Random.onUnitSphere * i;
				}

				// Schedule jobs
				Debug.Log("[JobScheduler] ===== SCHEDULING JOBS =====");
				ScheduleAllJobs();

				Debug.Log($"[JobScheduler] Total scheduled: {totalJobCount} jobs " +
				          $"({regularJobCount} regular, {jobForCount} JobFor, {parallelJobCount} ParallelFor)");
			}
			catch (Exception e)
			{
				Debug.LogError($"[JobScheduler] Error in Start: {e.Message}\n{e.StackTrace}");
			}
		}

		private void ScheduleAllJobs()
		{
			// Reset counters
			regularJobCount = 0;
			jobForCount = 0;
			parallelJobCount = 0;
			
			// Schedule regular IJob instances
			Debug.Log("[JobScheduler] Scheduling regular jobs");
			for (var i = 0; i < 100; i++)
			{
				var job = new TestJob
				{
					A = i,
					B = -i
				};
				scheduler.AddJob(job);
				regularJobCount++;
			}
			Debug.Log($"[JobScheduler] Added {regularJobCount} regular jobs");

			// Schedule IJobFor instances
			Debug.Log("[JobScheduler] Scheduling JobFor jobs");
			for (var i = 0; i < 100; i++)
			{
				var job = new TestJobFor
				{
					Input = inputArray,
					Multiplier = 5.5f + i
				};
				scheduler.AddJob(job, inputArray.Length);
				jobForCount++;
			}
			Debug.Log($"[JobScheduler] Added {jobForCount} JobFor jobs (array length: {inputArray.Length})");

			// Schedule IJobParallelFor instances
			Debug.Log("[JobScheduler] Scheduling ParallelFor jobs");
			for (var i = 0; i < 100; i++)
			{
				var job = new TestJobParallelFor
				{
					Positions = positionArray,
					DeltaTime = Time.deltaTime,
					Force = new Vector3(i, i, i)
				};
				scheduler.AddJobParallel(job, positionArray.Length, 10);
				parallelJobCount++;
			}

			var jobC = new TestOutput()
			           {
				           A = a,
				           B = b
			           };
			scheduler.AddJob(jobC);
			regularJobCount++;
			Debug.Log($"[JobScheduler] Added {parallelJobCount} ParallelFor jobs (array length: {positionArray.Length})");
			
			// Calculate total
			totalJobCount = regularJobCount + jobForCount + parallelJobCount;
		}

		private async void Update()
		{
			try
			{
				// Only schedule if we have jobs to schedule and haven't scheduled them yet
				if (scheduler.HasAnyJobsToSchedule <= 0 || !sw.IsRunning) return;
				Debug.Log("[JobScheduler] Scheduling all jobs to run on worker threads");
				float startTime = Time.realtimeSinceStartup;
					
				await scheduler.ScheduleAll();
					
				float duration = Time.realtimeSinceStartup - startTime;
				Debug.Log($"[JobScheduler] All jobs scheduled in {duration:F4} seconds");
			}
			catch (Exception e)
			{
				Debug.LogError($"[JobScheduler] Error in Update: {e.Message}\n{e.StackTrace}");
			}
		}

		private async void LateUpdate()
		{
			try
			{
				// Only try to complete jobs if we have pending jobs
				if (scheduler.HasPendingJobs <= 0) return;
				Debug.Log("[JobScheduler] Waiting for all jobs to complete");
				float startTime = Time.realtimeSinceStartup;
					
				await scheduler.Complete();
					
				float duration = Time.realtimeSinceStartup - startTime;
				sw.Stop();
				Debug.Log($"[JobScheduler] All jobs completed in {duration:F4} seconds");
				Debug.Log($"[JobScheduler] Total execution time: {sw.ElapsedMilliseconds}ms for {totalJobCount} jobs");
				unsafe
				{
					Debug.Log($"JOB OUTPUT TEST: >>>> {b[0]} <<<<");
				}
			}
			catch (Exception e)
			{
				Debug.LogError($"[JobScheduler] Error in LateUpdate: {e.Message}\n{e.StackTrace}");
			}
		}

		private void OnDestroy()
		{
			try
			{
				Debug.Log("[JobScheduler] Cleaning up resources");
				
				// Dispose native collections
				if (inputArray.IsCreated)
				{
					inputArray.Dispose();
					Debug.Log("[JobScheduler] Disposed inputArray");
				}

				if (positionArray.IsCreated)
				{
					positionArray.Dispose();
					Debug.Log("[JobScheduler] Disposed positionArray");
				}

				// Dispose scheduler
				scheduler.Dispose();
				Debug.Log("[JobScheduler] Disposed job scheduler");
				
				if (sw.IsRunning)
				{
					sw.Stop();
				}
			}
			catch (Exception e)
			{
				Debug.LogError($"[JobScheduler] Error during cleanup: {e.Message}\n{e.StackTrace}");
			}
		}
	}
}