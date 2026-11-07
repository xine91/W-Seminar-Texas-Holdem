using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PokerSimPlatform.Core.Simulation;

namespace PokerSimPlatform.Web.Services;

public enum JobStatus { Queued, Running, Completed, Failed, Cancelled }

public sealed record JobSnapshot(
    string Id, string Status, int Current, int Total, string? SessionId, int? Seed,
    string? Error, int Written, double DurationSeconds, bool IsFinished);

public sealed class SimulationJob
{
    private int _status;
    private int _current;
    private int _total;

    public SimulationJob(string id, SimulationRequest request)
    {
        Id = id;
        Request = request;
        Created = DateTime.UtcNow;
    }

    public string Id { get; }
    public SimulationRequest Request { get; }
    public DateTime Created { get; }
    public DateTime? Started { get; set; }
    public DateTime? Finished { get; set; }
    public string? SessionId { get; set; }
    public int? Seed { get; set; }
    public string? Error { get; set; }
    public int Written { get; set; }
    public CancellationTokenSource Cts { get; } = new();

    public JobStatus Status
    {
        get => (JobStatus)Volatile.Read(ref _status);
        set => Volatile.Write(ref _status, (int)value);
    }

    public int Current => Volatile.Read(ref _current);
    public int Total => Volatile.Read(ref _total);
    public void SetProgress(int current, int total)
    {
        Volatile.Write(ref _current, current);
        Volatile.Write(ref _total, total);
    }

    public bool IsFinished => Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled;

    public JobSnapshot ToSnapshot()
    {
        var end = Finished ?? DateTime.UtcNow;
        var duration = Started.HasValue ? (end - Started.Value).TotalSeconds : 0;
        return new JobSnapshot(Id, Status.ToString(), Current, Total, SessionId, Seed, Error, Written, duration, IsFinished);
    }
}

/// <summary>
/// Hintergrund-Warteschlange für Simulationen: der HTTP-Request kehrt sofort zurück,
/// Fortschritt wird per Polling/SSE abgefragt, Jobs sind abbrechbar. Jobs laufen nacheinander
/// (jeder Job nutzt intern bereits alle CPU-Kerne).
/// </summary>
public sealed class SimulationJobManager : BackgroundService
{
    private readonly Channel<SimulationJob> _queue = Channel.CreateUnbounded<SimulationJob>();
    private readonly ConcurrentDictionary<string, SimulationJob> _jobs = new();
    private readonly ResultStore _store;
    private readonly EquityCache _cache;
    private readonly ILogger<SimulationJobManager> _logger;

    public SimulationJobManager(ResultStore store, EquityCache cache, ILogger<SimulationJobManager> logger)
    {
        _store = store;
        _cache = cache;
        _logger = logger;
    }

    public SimulationJob Enqueue(SimulationRequest request)
    {
        PruneOldJobs();
        var job = new SimulationJob(Guid.NewGuid().ToString("N")[..12], request);
        _jobs[job.Id] = job;
        _queue.Writer.TryWrite(job);
        _logger.LogInformation("Job {Id} eingereiht.", job.Id);
        return job;
    }

    public SimulationJob? Get(string id) => _jobs.TryGetValue(id, out var job) ? job : null;

    public bool Cancel(string id)
    {
        var job = Get(id);
        if (job == null || job.IsFinished) return false;
        job.Cts.Cancel();
        return true;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            if (job.Cts.IsCancellationRequested)
            {
                job.Status = JobStatus.Cancelled;
                job.Finished = DateTime.UtcNow;
                continue;
            }
            await Task.Run(() => Process(job, stoppingToken), CancellationToken.None);
        }
    }

    private void Process(SimulationJob job, CancellationToken hostToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(job.Cts.Token, hostToken);
        job.Status = JobStatus.Running;
        job.Started = DateTime.UtcNow;
        try
        {
            var runner = new SimulationRunner(_logger, _cache);
            job.SessionId = runner.SessionId;
            var results = runner.Run(job.Request, new SyncProgress(p => job.SetProgress(p.Current, p.Total)), linked.Token);
            job.Seed = runner.Seed;
            _store.Append(results);
            job.Written = results.Count;
            job.SetProgress(job.Total, job.Total);
            job.Status = JobStatus.Completed;
            _logger.LogInformation("Job {Id} abgeschlossen: {Count} Ergebnisse (Session {Session}, Seed {Seed}).",
                job.Id, results.Count, runner.SessionId, runner.Seed);
        }
        catch (OperationCanceledException)
        {
            job.Status = JobStatus.Cancelled;
            _logger.LogInformation("Job {Id} abgebrochen.", job.Id);
        }
        catch (Exception ex)
        {
            job.Error = ex.Message;
            job.Status = JobStatus.Failed;
            _logger.LogError(ex, "Job {Id} fehlgeschlagen.", job.Id);
        }
        finally
        {
            job.Finished = DateTime.UtcNow;
        }
    }

    private void PruneOldJobs()
    {
        var limit = DateTime.UtcNow.AddHours(-1);
        foreach (var kv in _jobs.Where(kv => kv.Value.IsFinished && kv.Value.Finished < limit).ToList())
            _jobs.TryRemove(kv.Key, out _);
    }

    /// <summary>IProgress ohne SynchronizationContext-Umweg (Progress&lt;T&gt; würde über den ThreadPool posten).</summary>
    private sealed class SyncProgress(Action<SimulationProgress> action) : IProgress<SimulationProgress>
    {
        public void Report(SimulationProgress value) => action(value);
    }
}
