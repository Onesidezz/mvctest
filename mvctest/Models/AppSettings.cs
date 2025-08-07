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
        public string? CSVFilePath { get; set; }
        public string Classificationjson { get; set; }
        public string RetentionOfFive { get; set; }
        public string RetentionOfTen { get; set; }
        public string RetentionOfOne { get; set; }
        public string RetentionOfTwo { get; set; }
        public string RetentionOfThree { get; set; }
        public string RetentionOfSeven { get; set; }
        public string EmbeddingModelPath { get; set; }
    }
}
