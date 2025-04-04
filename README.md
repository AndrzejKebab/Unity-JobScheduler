
# Unity JobScheduler

## Warning
This is WIP and yet not **fully** tested!

### Example in use
1.0.0
BatchSize = 16

[working example 1.0.0](https://github.com/user-attachments/assets/f4e8e3b4-e6ab-4e32-be8a-aefc0032b5ba)

1.1.0

Default BatchSize

[working example b32 1.1.0](https://github.com/user-attachments/assets/9a26016b-17f6-4d58-8bd1-10ae466fb89e)

BatchSize = 16

[working example b16 1.1.0](https://github.com/user-attachments/assets/1ca6ac7f-5f3b-4b2e-9649-46e54b09d1de)

1.1.1
BatchSize = 16

![image](https://github.com/user-attachments/assets/ba4cd777-904e-4e6d-ae3d-6f3526a42f46)


## Overview

The `JobScheduler` system provides a Burst-compatible framework for efficiently managing Unity's job system. It offers specialized schedulers for different job types (`IJob`, `IJobFor`, and `IJobParallelFor`), with batched scheduling and completion to minimize main thread blocking.

## Dependencies

This package requires the following dependencies:

- **Unity.Jobs**: Core Unity package for the C# Job System
- **Unity.Collections**: Provides NativeList and other Burst-compatible collections
- **Unity.Burst**: For high-performance native code compilation
- [**UniTask**](https://github.com/Cysharp/UniTask): For efficient asynchronous operations

Add these dependencies to your project's `manifest.json`:

```json
{
  "dependencies": {
    "com.unity.burst": "1.8.4",
    "com.unity.collections": "1.5.1",
    "com.unity.jobs": "0.70.0-preview.7",
    "com.cysharp.unitask": "2.3.3",
  },
  "scopedRegistries": [
    {
      "name": "package.openupm.com",
      "url": "https://package.openupm.com",
      "scopes": [
        "com.cysharp.unitask",
      ]
    }
  ]
}
```

## Key Features

- **Batched Processing**: Automatically yields to the main thread to prevent blocking
- **Burst Compatibility**: All schedulers are designed to work with Burst compilation
- **Memory Safety**: Proper disposal of native collections
- **Async/Await Support**: Uses UniTask for efficient asynchronous operations
- **Specialized Schedulers**: Optimized implementations for different job types
- **Interface-Based Design**: Consistent API across all scheduler types through interfaces

## Core Interfaces

### IJobData

The core interface that standardizes job scheduling operations:

```csharp
public interface IJobData
{
    public JobHandle Schedule();
}
```

### IJobScheduler

Common interface implemented by all job schedulers:

```csharp
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
```

## Scheduler Types

### JobSchedulerBase

Base implementation that provides common functionality for all job schedulers:

- Tracking job handles
- Batched completion with yielding
- Resource management

```csharp
public struct JobSchedulerBase : IDisposable
```

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `BatchSize` | `byte` | Controls how many jobs are processed before yielding back to the main thread. Default is 32. |
| `AreJobsCompleted` | `bool` | Returns true if all tracked jobs have completed. |
| `JobHandlesCount` | `int` | Returns the number of tracked job handles. |

#### Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `AddJobHandle(JobHandle handle)` | `void` | Adds an external job handle to the tracking list. |
| `CompleteAsync()` | `UniTask` | Completes all tracked jobs in batches, yielding between batches. |
| `CompleteImmediate()` | `void` | Completes all tracked jobs without yielding. |
| `Dispose()` | `void` | Completes all jobs and releases resources. |

### JobData<T>

Data structure for wrapping IJob implementations:

```csharp
public struct JobData<T> : IJobData where T : struct, IJob
```

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Job` | `T` | The job to be scheduled. |
| `Dependency` | `JobHandle` | Optional job handle that must complete before this job can start. |

#### Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `Schedule()` | `JobHandle` | Schedules the job with the specified dependency. |

### JobScheduler\<T>

Specialized scheduler for `IJob` implementations:

- List-based job management
- Batched scheduling and completion

```csharp
public struct JobScheduler<T> : IJobScheduler, IDisposable where T : unmanaged, IJob
```

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `BatchSize` | `byte` | Controls how many jobs are processed before yielding. Delegates to base scheduler. |
| `ScheduledJobs` | `int` | Returns the number of jobs that have been scheduled. |
| `JobsToSchedule` | `int` | Returns the number of jobs in the queue waiting to be scheduled. |
| `JobsCount` | `int` | Returns the total number of jobs being managed by the scheduler. |
| `AreJobsCompleted` | `bool` | Returns true if all tracked jobs have completed. |

#### Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `AddJob(T job, JobHandle dependency = default)` | `void` | Adds a job to the queue for scheduling with optional dependency. |
| `AddJobHandle(JobHandle handle)` | `void` | Adds an external job handle to the tracking list. |
| `ScheduleJobsAsync()` | `UniTask` | Schedules all queued jobs in batches, yielding between batches. |
| `CompleteAsync()` | `UniTask` | Completes all tracked jobs in batches. Delegates to base scheduler. |
| `CompleteImmediate()` | `void` | Completes all tracked jobs without yielding. Delegates to base scheduler. |
| `Dispose()` | `void` | Completes all jobs and releases resources. |

### JobForScheduler\<T>

Specialized scheduler for `IJobFor` implementations:

- Manages jobs that process arrays of data sequentially
- Configurable array length per job

```csharp
public struct JobForScheduler<T> : IJobScheduler, IDisposable where T : unmanaged, IJobFor
```

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `BatchSize` | `byte` | Controls how many jobs are processed before yielding. Delegates to base scheduler. |
| `ScheduledJobs` | `int` | Returns the number of jobs that have been scheduled. |
| `JobsToSchedule` | `int` | Returns the number of jobs in the queue waiting to be scheduled. |
| `JobsCount` | `int` | Returns the total number of jobs being managed by the scheduler. |
| `AreJobsCompleted` | `bool` | Returns true if all tracked jobs have completed. |

#### Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `AddJob(T job, int arrayLength, JobHandle dependency = default)` | `void` | Adds a job to the queue with specified array length and optional dependency. |
| `AddJobHandle(JobHandle handle)` | `void` | Adds an external job handle to the tracking list. |
| `ScheduleJobsAsync()` | `UniTask` | Schedules all queued jobs in batches, yielding between batches. |
| `CompleteAsync()` | `UniTask` | Completes all tracked jobs in batches. Delegates to base scheduler. |
| `CompleteImmediate()` | `void` | Completes all tracked jobs without yielding. Delegates to base scheduler. |
| `Dispose()` | `void` | Completes all jobs and releases resources. |

### JobParallelForScheduler\<T>

Specialized scheduler for `IJobParallelFor` implementations:

- Manages jobs that process arrays of data in parallel
- Configurable array length and inner batch size per job

```csharp
public struct JobParallelForScheduler<T> : IJobScheduler, IDisposable where T : unmanaged, IJobParallelFor
```

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `BatchSize` | `byte` | Controls how many jobs are processed before yielding. Delegates to base scheduler. |
| `ScheduledJobs` | `int` | Returns the number of jobs that have been scheduled. |
| `JobsToSchedule` | `int` | Returns the number of jobs in the queue waiting to be scheduled. |
| `JobsCount` | `int` | Returns the total number of jobs being managed by the scheduler. |
| `AreJobsCompleted` | `bool` | Returns true if all tracked jobs have completed. |

#### Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `AddJob(T job, int arrayLength, int innerBatchSize = 64, JobHandle dependency = default)` | `void` | Adds a job to the queue with specified array length, inner batch size, and optional dependency. |
| `AddJobHandle(JobHandle handle)` | `void` | Adds an external job handle to the tracking list. |
| `ScheduleJobsAsync()` | `UniTask` | Schedules all queued jobs in batches, yielding between batches. |
| `CompleteAsync()` | `UniTask` | Completes all tracked jobs in batches. Delegates to base scheduler. |
| `CompleteImmediate()` | `void` | Completes all tracked jobs without yielding. Delegates to base scheduler. |
| `Dispose()` | `void` | Completes all jobs and releases resources. |

## Usage Examples

### Using JobScheduler\<T> with IJob

```csharp
using PatataGames.JobScheduler;
using Unity.Jobs;

// Create a job
struct MyJob : IJob
{
    public void Execute() 
    {
        // Job implementation
    }
}

// Create a scheduler
var scheduler = new JobScheduler<MyJob>();

// Add jobs with optional dependencies
scheduler.AddJob(new MyJob());
scheduler.AddJob(new MyJob(), dependencyHandle);

// Schedule and complete
await scheduler.ScheduleJobsAsync();
await scheduler.CompleteAsync();

// Dispose when done
scheduler.Dispose();
```

### Using JobForScheduler\<T> with IJobFor

```csharp
using PatataGames.JobScheduler;
using Unity.Jobs;

// Create a job
struct MyJobFor : IJobFor
{
    public void Execute(int index) 
    {
        // Job implementation
    }
}

// Create a scheduler
var scheduler = new JobForScheduler<MyJobFor>();

// Add jobs with array length and optional dependency
scheduler.AddJob(new MyJobFor(), arrayLength: 100, dependency: default);

// Schedule and complete
await scheduler.ScheduleJobsAsync();
await scheduler.CompleteAsync();

// Dispose when done
scheduler.Dispose();
```

### Using JobParallelForScheduler\<T> with IJobParallelFor

```csharp
using PatataGames.JobScheduler;
using Unity.Jobs;

// Create a job
struct MyParallelJob : IJobParallelFor
{
    public void Execute(int index) 
    {
        // Job implementation
    }
}

// Create a scheduler
var scheduler = new JobParallelForScheduler<MyParallelJob>();

// Add jobs with array length, inner batch size, and optional dependency
scheduler.AddJob(new MyParallelJob(), arrayLength: 1000, innerBatchSize: 64);

// Schedule and complete
await scheduler.ScheduleJobsAsync();
await scheduler.CompleteAsync();

// Dispose when done
scheduler.Dispose();
```

## Advanced Usage

### Using External Job Handles

All scheduler types support tracking external job handles:

```csharp
using PatataGames.JobScheduler;
using Unity.Jobs;

// Create a scheduler
var scheduler = new JobScheduler<MyJob>();

// Schedule a job directly and get its handle
JobHandle externalHandle = someJob.Schedule();

// Add the external handle to the scheduler
scheduler.AddJobHandle(externalHandle);

// Complete all jobs including the external one
await scheduler.CompleteAsync();
```

### Controlling Batch Size

```csharp
var scheduler = new JobScheduler<MyJob>(capacity: 64, batchSize: 16);
// Or set it after creation
scheduler.BatchSize = 32; // Process 32 jobs before yielding
```

### Checking Job Completion Status

```csharp
using PatataGames.JobScheduler;

var scheduler = new JobScheduler<MyJob>();
// Add and schedule jobs...

// Check if all jobs are completed
if (scheduler.AreJobsCompleted)
{
    // All jobs are done
}

// Check how many jobs are queued vs scheduled
int pendingJobs = scheduler.JobsCount;
int scheduledJobs = scheduler.ScheduledJobs;
int queuedJobs = scheduler.JobsToSchedule;
```

### Immediate Completion

```csharp
var scheduler = new JobScheduler<MyJob>();
// Add and schedule jobs...

// Complete all jobs immediately without yielding
scheduler.CompleteImmediate();
```

## Performance Considerations

1. **BatchSize**: Adjust the `BatchSize` property to balance responsiveness and overhead. Smaller values yield more frequently but add more overhead.

2. **InnerBatchSize**: For parallel jobs, the inner batch size controls how many iterations each worker thread processes. Adjust based on workload characteristics.

3. **Memory Management**: Always call `Dispose()` when done with a scheduler to prevent memory leaks.

4. **Burst Compatibility**: All schedulers are designed to be Burst-compatible for maximum performance.

## Implementation Notes

- The system uses a common interface (`IJobScheduler`) and composition rather than inheritance.
- All schedulers implement `IDisposable` to ensure proper cleanup of native collections.
- Job data structures implement `IJobData` to ensure consistent scheduling behavior.
- `UniTask` integration allows for efficient asynchronous operation without blocking the main thread.
- The `[BurstCompile]` attribute is applied to methods that can benefit from Burst compilation.

## Thread Safety

- The schedulers are designed to be used from the main thread.
- The underlying job system handles thread safety for job execution.
- Native collections are not thread-safe for concurrent writing, so avoid modifying the scheduler from multiple threads.
