using Cysharp.Threading.Tasks;
using Unity.Jobs;

namespace PatataGames.JobScheduler
{
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