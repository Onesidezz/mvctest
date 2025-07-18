using Microsoft.ML.Data;

namespace mvctest.Models
{
    public class ChatBot
    {
        public class ChatInput
        {
            public string UserMessage { get; set; }
        }

        public class ChatResponse
        {
            public string ResponseMessage { get; set; }
        }

        public class ChatData
        {
            [LoadColumn(0)]
            public string Text { get; set; }
            [LoadColumn(1)]
            public string Label { get; set; }
            //[LoadColumn(2)]
            //public string Intent { get; set; }
            [LoadColumn(3)]
            public string Entities { get; set; }
        }
       
        public class ChatPrediction
        {
            [ColumnName("PredictedIntent")]
            public string PredictedIntent { get; set; }
        }
        public class SentenceData
        {
            public string Sentence { get; set; }
            public bool IsSummary { get; set; } // Label
        }

        public class SentencePrediction
        {
            [ColumnName("PredictedLabel")]
            public bool Prediction { get; set; }
            public float Probability { get; set; }
        }

    }
}
