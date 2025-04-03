using Cysharp.Threading.Tasks;
using Unity.Jobs;

namespace PatataGames.JobScheduler
{
	/// <summary>
	///     Defines the common interface for all job schedulers.
	///     Provides methods and properties for scheduling, tracking, and completing Unity jobs.
	/// </summary>
	public interface IJobScheduler
	{		
		public byte BatchSize        { get; }
		public int  ScheduledJobs    { get; }
		public int  JobsToSchedule   { get; }
		public int  JobsCount        { get; }
		public bool AreJobsCompleted { get; }

		public void    AddJobHandle(JobHandle handle);
		public UniTask ScheduleJobsAsync();
		public UniTask CompleteAsync();
		public void    CompleteImmediate();
	}
}