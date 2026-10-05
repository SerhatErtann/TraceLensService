namespace TraceLensService.Models.Responses.Shared
{
    public class PagedResponse<T>
    {
        public List<T> Items { get; set; } = [];
        public long Total { get; set; }
        public int Limit { get; set; }
        public int Offset { get; set; }
    }
}
