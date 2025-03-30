# Unity JobScheduler

## Warning
This is WIP and yet not tested!

## Overview

The `JobScheduler` system provides a Burst-compatible framework for efficiently managing Unity's job system. It offers specialized schedulers for different job types (`IJob`, `IJobFor`, and `IJobParallelFor`), with batched scheduling and completion to minimize main thread blocking.

## Dependencies

This package requires the following dependencies:

- **Unity.Jobs**: Core Unity package for the C# Job System
- **Unity.Collections**: Provides NativeList and other Burst-compatible collections
- **Unity.Burst**: For high-performance native code compilation
- [**UniTask**](https://github.com/Cysharp/UniTask): For efficient asynchronous operations
- [**ZLinq**](https://github.com/Cysharp/ZLinq): Used for allocation-free LINQ-like operations on collections

Add these dependencies to your project's `manifest.json`:

```json
{
  "dependencies": {
    "com.unity.burst": "1.8.4",
    "com.unity.collections": "1.5.1",
    "com.unity.jobs": "0.70.0-preview.7",
    "com.cysharp.unitask": "2.3.3",
    "com.zlinq": "1.0.0"
  },
  "scopedRegistries": [
    {
      "name": "package.openupm.com",
      "url": "https://package.openupm.com",
      "scopes": [
        "com.cysharp.unitask",
        "com.zlinq"
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
| `BatchSize` | `byte` | Controls how many jobs are processed before yielding back to the main thread. Default is 8. |
| `AreJobsCompleted` | `bool` | Returns true if all tracked jobs have completed. |

#### Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `ScheduleJob(JobHandle handle)` | `void` | Adds an external job handle to the tracking list. |
| `Complete()` | `UniTask` | Completes all tracked jobs in batches, yielding between batches. |
| `CompleteAll()` | `void` | Completes all tracked jobs without yielding. |
| `Dispose()` | `void` | Completes all jobs and releases resources. |
| `GetJobHandlesCount()` | `int` | Returns the number of tracked job handles. |

### JobScheduler\<T>

Specialized scheduler for `IJob` implementations:

- Queue-based job management
- Batched scheduling and completion

```csharp
public struct JobScheduler<T> : IDisposable where T : unmanaged, IJob
```

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `BatchSize` | `byte` | Controls how many jobs are processed before yielding. Delegates to base scheduler. |

#### Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `AddJob(T job)` | `void` | Adds a job to the queue for scheduling. |
| `ScheduleJob(JobHandle handle)` | `void` | Adds an external job handle to the tracking list. |
| `ScheduleAll()` | `UniTask` | Schedules all queued jobs in batches, yielding between batches. |
| `Complete()` | `UniTask` | Completes all tracked jobs in batches. Delegates to base scheduler. |
| `CompleteAll()` | `void` | Completes all tracked jobs without yielding. Delegates to base scheduler. |
| `Dispose()` | `void` | Completes all jobs and releases resources. |

### JobForScheduler\<T>

Specialized scheduler for `IJobFor` implementations:

- Manages jobs that process arrays of data sequentially
- Configurable array length per job

```csharp
public struct JobForScheduler<T> : IDisposable where T : unmanaged, IJobFor
```

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `BatchSize` | `byte` | Controls how many jobs are processed before yielding. Delegates to base scheduler. |

#### Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `AddJob(T job, int arrayLength)` | `void` | Adds a job to the queue with specified array length. |
| `ScheduleJob(JobHandle handle)` | `void` | Adds an external job handle to the tracking list. |
| `ScheduleAll()` | `UniTask` | Schedules all queued jobs in batches, yielding between batches. |
| `Complete()` | `UniTask` | Completes all tracked jobs in batches. Delegates to base scheduler. |
| `CompleteAll()` | `void` | Completes all tracked jobs without yielding. Delegates to base scheduler. |
| `Dispose()` | `void` | Completes all jobs and releases resources. |

### JobParallelForScheduler\<T>

Specialized scheduler for `IJobParallelFor` implementations:

- Manages jobs that process arrays of data in parallel
- Configurable array length and inner batch size per job

```csharp
public struct JobParallelForScheduler<T> : IDisposable where T : unmanaged, IJobParallelFor
```

#### Properties

| Property | Type | Description |
|----------|------|-------------|
| `BatchSize` | `byte` | Controls how many jobs are processed before yielding. Delegates to base scheduler. |

#### Methods

| Method | Return Type | Description |
|--------|-------------|-------------|
| `AddJob(T job, int arrayLength, int innerBatchSize = 64)` | `void` | Adds a job to the queue with specified array length and inner batch size. |
| `ScheduleJob(JobHandle handle)` | `void` | Adds an external job handle to the tracking list. |
| `ScheduleAll()` | `UniTask` | Schedules all queued jobs in batches, yielding between batches. |
| `Complete()` | `UniTask` | Completes all tracked jobs in batches. Delegates to base scheduler. |
| `CompleteAll()` | `void` | Completes all tracked jobs without yielding. Delegates to base scheduler. |
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

// Add jobs
scheduler.AddJob(new MyJob());
scheduler.AddJob(new MyJob());

// Schedule and complete
await scheduler.ScheduleAll();
await scheduler.Complete();

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

// Add jobs with array length
scheduler.AddJob(new MyJobFor(), arrayLength: 100);

// Schedule and complete
await scheduler.ScheduleAll();
await scheduler.Complete();

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

// Add jobs with array length and inner batch size
scheduler.AddJob(new MyParallelJob(), arrayLength: 1000, innerBatchSize: 64);

// Schedule and complete
await scheduler.ScheduleAll();
await scheduler.Complete();

// Dispose when done
scheduler.Dispose();
```

## Advanced Usage

### Using External Job Handles

All scheduler types support tracking external job handles, useful if you want to schedule job immediately:

```csharp
using PatataGames.JobScheduler;
using Unity.Jobs;

// Create a scheduler
var scheduler = new JobScheduler<MyJob>();

// Schedule a job directly and get its handle
JobHandle externalHandle = someJob.Schedule();

// Add the external handle to the scheduler
scheduler.ScheduleJob(externalHandle);

// Complete all jobs including the external one
await scheduler.Complete();
```

### Controlling Batch Size

```csharp
var scheduler = new JobScheduler<MyJob>();
scheduler.BatchSize = 16; // Process 16 jobs before yielding
```

### Checking Job Completion Status

```csharp
using PatataGames.JobScheduler;

var baseScheduler = new JobSchedulerBase();
// Add job handles...

// Check if all jobs are completed
if (baseScheduler.AreJobsCompleted)
{
    // All jobs are done
}
```

### Immediate Completion

```csharp
var scheduler = new JobScheduler<MyJob>();
// Add and schedule jobs...

// Complete all jobs immediately without yielding
scheduler.CompleteAll();
```

## Performance Considerations

1. **BatchSize**: Adjust the `BatchSize` property to balance responsiveness and overhead. Smaller values yield more frequently but add more overhead.

2. **InnerBatchSize**: For parallel jobs, the inner batch size controls how many iterations each worker thread processes. Adjust based on workload characteristics.

3. **Memory Management**: Always call `Dispose()` when done with a scheduler to prevent memory leaks.

4. **Burst Compatibility**: All schedulers are designed to be Burst-compatible for maximum performance.

## Implementation Notes

- The system uses composition rather than inheritance, with specialized schedulers delegating common functionality to `JobSchedulerBase`.
- All schedulers implement `IDisposable` to ensure proper cleanup of native collections.
- `UniTask` integration allows for efficient asynchronous operation without blocking the main thread.
- The `[BurstCompile]` attribute is applied to methods that can benefit from Burst compilation.

## Thread Safety

- The schedulers are designed to be used from the main thread.
- The underlying job system handles thread safety for job execution.
- Native collections are not thread-safe for concurrent writing, so avoid modifying the scheduler from multiple threads.