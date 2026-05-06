namespace Datavery.Domain.Models
{
    public class QueryResponse
    {
        public string Question { get; set; } = "";
        public List<string> Datasets { get; set; } = new();
        public string Category { get; set; } = "";
        public bool IsJoin { get; set; }
        public string JoinHint { get; set; } = "";
        public string Status { get; set; } = "success";
        public string Message { get; set; } = "";
        public List<Dictionary<string, object>> Results { get; set; } = new();
    }
}