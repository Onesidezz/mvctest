namespace mvctest.Models
{
    public class AppSettings
    {
        public string WorkGroupUrl { get; set; }
        public string DataSetID { get; set; }
        public string TraningDataPath { get; set; }
        public string TrainedModelPath { get; set; }
        public string DefaultDocumnetType { get; set; }
        public string DeepseekApiKey { get; set; }
        public string ChatGptApiKey { get; set; }
        public string TrainedModelPathSummarize { get; set; }
        public int EstimatedRecordCount { get; set; }
        public string FolderDirectory { get; set; }
        public string IndexDirectory { get; set; }

    }
}
