using System.Collections.Concurrent;

namespace TrackForge.Core;

public enum JobState { Queued, Running, Done, Failed, Cancelled }

public sealed class Job
{
    public int Id { get; init; }
    public string Label { get; set; } = "";
    public string Kind { get; set; } = "";
    public JobState State { get; set; } = JobState.Queued;
    public double Progress { get; set; }
    public string Message { get; set; } = "Queued";
    public DateTime Created { get; } = DateTime.Now;
    public object? Result { get; set; }
    public string? Error { get; set; }

    internal CancellationTokenSource Cts { get; } = new();
    internal Func<Job, CancellationToken, Task> Work { get; init; } = (_, _) => Task.CompletedTask;

    public void Cancel() { try { Cts.Cancel(); } catch { } }

    public string StateText => State switch
    {
        JobState.Queued => "Queued",
        JobState.Running => $"{Progress:0}%",
        JobState.Done => "Done",
        JobState.Failed => "Failed",
        _ => "Cancelled"
    };
}

/// <summary>
/// Runs jobs on a bounded number of background workers and raises events on
/// whatever thread finished. The UI marshals to the form itself.
/// </summary>
public sealed class JobQueue : IDisposable
{
    private readonly BlockingCollection<Job> _queue = new();
    private readonly List<Thread> _workers = new();
    private readonly ConcurrentDictionary<int, Job> _jobs = new();
    private int _seq;

    public event Action<Job>? JobChanged;

    public IReadOnlyCollection<Job> Jobs => _jobs.Values.OrderByDescending(j => j.Id).ToList();
    public int ActiveCount => _jobs.Values.Count(j => j.State is JobState.Queued or JobState.Running);

    public JobQueue(int workers = 2)
    {
        for (int i = 0; i < Math.Max(1, workers); i++)
        {
            var t = new Thread(WorkerLoop) { IsBackground = true, Name = $"TrackForge worker {i + 1}" };
            _workers.Add(t);
            t.Start();
        }
    }

    public Job Enqueue(string kind, string label, Func<Job, CancellationToken, Task> work)
    {
        var job = new Job
        {
            Id = Interlocked.Increment(ref _seq),
            Kind = kind,
            Label = label,
            Work = work,
        };
        _jobs[job.Id] = job;
        Notify(job);
        _queue.Add(job);
        return job;
    }

    public void Report(Job job, double progress, string message)
    {
        job.Progress = progress;
        job.Message = message;
        Notify(job);
    }

    public void ClearFinished()
    {
        foreach (var j in _jobs.Values.Where(j => j.State is JobState.Done or JobState.Failed or JobState.Cancelled))
            _jobs.TryRemove(j.Id, out _);
        JobChanged?.Invoke(new Job { Id = 0, Label = "refresh" });
    }

    private void Notify(Job job)
    {
        try { JobChanged?.Invoke(job); } catch { }
    }

    private void WorkerLoop()
    {
        foreach (var job in _queue.GetConsumingEnumerable())
        {
            if (job.Cts.IsCancellationRequested)
            {
                job.State = JobState.Cancelled;
                job.Message = "Cancelled";
                Notify(job);
                continue;
            }

            job.State = JobState.Running;
            job.Message = "Starting";
            Notify(job);

            try
            {
                job.Work(job, job.Cts.Token).GetAwaiter().GetResult();
                if (job.State == JobState.Running)
                {
                    job.State = JobState.Done;
                    job.Progress = 100;
                    if (job.Message is "Starting" or "Queued") job.Message = "Done";
                }
            }
            catch (OperationCanceledException)
            {
                job.State = JobState.Cancelled;
                job.Message = "Cancelled";
            }
            catch (Exception ex)
            {
                job.State = JobState.Failed;
                job.Error = ex.ToString();
                job.Message = ex.Message.Length > 200 ? ex.Message[..200] : ex.Message;
            }

            Notify(job);
        }
    }

    public void Dispose()
    {
        try { _queue.CompleteAdding(); } catch { }
        foreach (var j in _jobs.Values) j.Cancel();
    }
}
