using Unity.Jobs;

namespace PatataGames.JobScheduler
{
	public interface IJobData
	{
		public JobHandle Schedule();
	}
}