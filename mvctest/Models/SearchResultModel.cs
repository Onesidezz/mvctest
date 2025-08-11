namespace mvctest.Models
{
    public class SearchResultModel
    {
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public string Content { get; set; } = string.Empty;
        public float Score { get; set; }
        public List<string> Snippets { get; set; }
        public string date { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
        
        // Additional properties for ML.NET advanced search
        public List<string> EntityMatches { get; set; } = new List<string>();
        public float SemanticSimilarity { get; set; } = 0.0f;
        public float Confidence { get; set; } = 0.0f;
        public Dictionary<string, float> MLFeatures { get; set; } = new Dictionary<string, float>();
    }


}
