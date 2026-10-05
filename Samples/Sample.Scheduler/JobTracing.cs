using System.Diagnostics;

namespace Sample.Scheduler;

/// <summary>
/// Her job çalıştırmasını TraceLens'in Schedulers sayfasında görünecek bir kök span içinde çalıştırır.
/// Job içindeki HTTP ve DB çağrıları otomatik olarak bu span'in altına düşer.
/// Bu dosyayı scheduler projenize kopyalayın (README → "Bir servisi TraceLens'e bağlamak").
/// </summary>
public static class JobTracing
{
    public const string SourceName = "TraceLens";

    private static readonly ActivitySource Source = new(SourceName);

    public static async Task RunAsync(string jobName, Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        // Her çalıştırma kendi trace'i olsun; dışarıdan bir parent'a bağlanmasın.
        Activity? previous = Activity.Current;
        Activity.Current = null;

        using Activity? activity = Source.StartActivity($"JOB {jobName}");
        activity?.SetTag("job.name", jobName);   // TraceLens job'ları bu etiketten tanır
        try
        {
            await work(cancellationToken);
            activity?.SetTag("job.status", "succeeded");
        }
        catch (Exception ex)
        {
            activity?.SetTag("job.status", "failed");
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
