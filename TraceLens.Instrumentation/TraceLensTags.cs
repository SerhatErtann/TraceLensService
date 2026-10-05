namespace TraceLens.Instrumentation;

/// <summary>Query API'nin okuduğu attribute adları. Değiştirirseniz sorguları da güncelleyin.</summary>
public static class TraceLensTags
{
    public const string AppType = "tracelens.app_type";
    public const string JobName = "job.name";
    public const string JobRunId = "job.run_id";
    public const string JobStatus = "job.status";
    public const string RequestBodySize = "http.request.body.size";
    public const string ResponseBodySize = "http.response.body.size";
}
