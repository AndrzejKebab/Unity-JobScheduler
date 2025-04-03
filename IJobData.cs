using Unity.Jobs;

namespace PatataGames.JobScheduler
{	
	/// <summary>
	///     Defines the common interface for job data structures.
	///     Provides a standardized way to schedule different types of Unity jobs.
	/// </summary>

	public interface IJobData
	{
		public JobHandle Schedule();
	}
}