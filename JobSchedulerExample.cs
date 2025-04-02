using Unity.Burst;
using Unity.Jobs;
using UnityEngine;

namespace PatataGames.JobScheduler
{
	#region Test jobs
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

	#endregion
	
	/// <summary>
	///     Example usage of JobSchedulerUnified
	/// </summary>
	public class JobSchedulerExample : MonoBehaviour
	{
		private JobSchedulerUnified scheduler;

		private void Start()
		{
			Debug.Log("Initialize.");
			scheduler = new JobSchedulerUnified(32, 4);
			Debug.Log("Create Jobs");
			// Example of adding jobs without scheduling them immediately
			var testJob1 = new TestJob();
			var testJob2 = new TestJobFor();
			var testJob3 = new TestJobParallelFor();
			Debug.Log("Adding Jobs...");
			var job1Index = scheduler.AddJob(testJob1);
			var job2Index = scheduler.AddJob(testJob2, 100);
			var job3Index = scheduler.AddJobParallel(testJob3, 1000, 64);

			Debug.Log("Scheduling Jobs...");
			// Later in your code, you can schedule these jobs
			scheduler.ScheduleJob(job1Index);

			// Or schedule all at once
			scheduler.ScheduleAll();
		}

		private void Update()
		{
			Debug.Log("Create Jobs");
			// Example usage with different job types (immediate scheduling)
			var testJob         = new TestJob();
			var testJobFor      = new TestJobFor();
			var testParallelJob = new TestJobParallelFor();

			// Schedule jobs immediately
			scheduler.Schedule(testJob);
			scheduler.Schedule(testJobFor, 100);
			scheduler.ScheduleParallel(testParallelJob, 1000, 64);

			Debug.Log("Completing");
			// Wait for all jobs to complete
			scheduler.CompleteAll();
		}

		private void OnDestroy()
		{
			Debug.Log("Destroying.");
			// Ensure proper cleanup
			scheduler.Dispose();
		}
	}
}