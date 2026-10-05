namespace TraceLensService.Models.Internal
{
    /// <summary>
    /// Servis haritasının ham kenarı. <see cref="Callee"/> karşı tarafın Server span'inden gelen servis adı;
    /// null ise karşı tarafta span yok ve <see cref="Host"/> (host:port veya "sqlite · main") kullanılır.
    /// </summary>
    public sealed record ServiceEdgeRow(
        string Caller, string? Callee, string Host, long Count, double AvgMs, double P95Ms, long ErrorCount);
}
