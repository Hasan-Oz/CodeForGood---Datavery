using Datavery.Domain.Enums;

namespace Datavery.Domain.Models
{
    public class DatasetSelection
    {
        public List<Dataset> Datasets { get; set; } = new();
        public string Category { get; set; } = "";
        public bool IsJoin { get; set; }
        public string JoinHint { get; set; } = "";
    }
}