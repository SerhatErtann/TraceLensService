using System.Diagnostics;

namespace TraceLens.Instrumentation;

public interface IJobTracer
{
    /// <summary>
    /// Job'u kök span içinde çalıştırır. Job içindeki HTTP/DB çağrıları bu span'in altına düşer
    /// ve job Schedulers sayfasında listelenir.
    /// </summary>
    Task RunAsync(string jobName, Func<CancellationToken, Task> work, CancellationToken cancellationToken = default);
}

public sealed class JobTracer : IJobTracer
{
    public async Task RunAsync(string jobName, Func<CancellationToken, Task> work, CancellationToken cancellationToken = default)
    {
        // Her çalıştırma kendi trace'i olmalı; dışarıdan sızan bir parent'a bağlanmasın.
        var previous = Activity.Current;
        Activity.Current = null;

        using var activity = TraceLensTracer.Source.StartActivity($"JOB {jobName}", ActivityKind.Internal);
        activity?.SetTag(TraceLensTags.JobName, jobName);
        activity?.SetTag(TraceLensTags.JobRunId, Guid.NewGuid().ToString("N"));

        try
        {
            await work(cancellationToken);
            activity?.SetTag(TraceLensTags.JobStatus, "succeeded");
        }
        catch (Exception ex)
        {
            activity?.SetTag(TraceLensTags.JobStatus, "failed");
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.AddException(ex);
            throw;
        }
        finally
        {
            Activity.Current = previous;
        }
    }
}
