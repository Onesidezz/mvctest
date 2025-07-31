namespace mvctest.Models
{
    public class SearchResultModel
    {
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public float Score { get; set; }
        public List<string> Snippets { get; set; }
        public string date { get; set; }    
    }


}
