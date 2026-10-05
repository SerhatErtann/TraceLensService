namespace TraceLensService.Models.Responses.ServiceDetail
{
    /// <summary>
    /// "Süre nereye gidiyor?" çubuğunun bir parçası. Her span'in kendi süresi (çocukları hariç) sayılır;
    /// dış çağrı ve DB span'leri karşı tarafta geçen süreyle birlikte tamamen kendi kategorisine yazılır.
    /// </summary>
    public class TimeSplitResponse
    {
        /// <summary>own (kendi kodu) | call (dış çağrılar) | db (veritabanı) | other</summary>
        public string Category { get; set; } = string.Empty;
        public double TotalMs { get; set; }
        /// <summary>0–1 arası pay.</summary>
        public double Share { get; set; }
    }
}
