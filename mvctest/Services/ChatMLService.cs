using Humanizer;
using iText.Commons.Actions.Contexts;
using Microsoft.Extensions.Options;
using Microsoft.ML;
using mvctest.Models;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using static mvctest.Models.ChatBot;

namespace mvctest.Services
{
    public class ChatMLService : IChatMLService
    {
        private readonly LocalSummarizationService _summaryService;

      
        
        private readonly MLContext _mlContext;
        private PredictionEngine<ChatData, ChatPrediction> _predictionEngine;
        private readonly AppSettings _settings;
        private readonly HttpClient _httpClient;
        private readonly IContentManager _contentManager;

        public ChatMLService(IOptions<AppSettings> options, HttpClient httpClient, IContentManager contentManager)
        {
            _settings = options.Value;
            _httpClient = httpClient;
            _mlContext = new MLContext();
            _summaryService = new LocalSummarizationService();
            if (File.Exists(_settings.TrainedModelPath))
            {
                using var stream = new FileStream(_settings.TrainedModelPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var model = _mlContext.Model.Load(stream, out var schema);
                _predictionEngine = _mlContext.Model.CreatePredictionEngine<ChatData, ChatPrediction>(model);
            }
            else
            {
                TrainModel();
            }

            _httpClient = httpClient;
            _contentManager = contentManager;
        }

        public async Task<string> GetChatBotResponse(string userMessage)
        {
            try
            {
                if (IsChatGPTSummaryRequest(userMessage, out string recordName))
                {
                    var record = _contentManager.GetRecordByTitle(recordName);
                    if (record != null)
                    {
                        //var summary = await _summaryService.GetSummaryAsync("", record.ESource);
                        var summary2 = await SummarizeWithStreaming( record.ESource);
                        return summary2;
                        //var response = await GetChatGptResponseAsync(userMessage, record.ESource);
                        //return response;
                    }
                    else
                    {
                        return $"Sorry, I couldn't find a record named \"{recordName}\".";
                    }
                }

                // Else use ML.NET prediction
                var input = new ChatData { Text = userMessage };
                var prediction = _predictionEngine.Predict(input);
                return prediction.PredictedIntent;
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }


        private bool IsChatGPTSummaryRequest(string message, out string recordName)
        {
            string[] triggers =
            {
                "Do you want a summary of this record? Please Enter Record Name :",
                "Which record summary do you want to know? Please Enter Record Name :",
                "Show me the record summary for Record Name :"
            };
            foreach (var trigger in triggers)
            {
                if (message.StartsWith(trigger, StringComparison.OrdinalIgnoreCase))
                {
                    recordName = message.Substring(trigger.Length).Trim();
                    return true;
                }
            }
            recordName = string.Empty;
            return false;
        }

        public void TrainModel()
        {
            var stopwatch = Stopwatch.StartNew();

            // Load data
            var trainingData = _mlContext.Data.LoadFromTextFile<ChatData>(
                path: _settings.TraningDataPath,
                hasHeader: true,
                separatorChar: ',');

            // Train using Intent as the label
            var intentPipeline = _mlContext.Transforms.Text.FeaturizeText("Features", nameof(ChatData.Text))
                .Append(_mlContext.Transforms.Conversion.MapValueToKey("Label", nameof(ChatData.Label)))
                .Append(_mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy(
                    labelColumnName: "Label",
                    featureColumnName: "Features"))
                .Append(_mlContext.Transforms.Conversion.MapKeyToValue("PredictedIntent", "PredictedLabel"));

            var model = intentPipeline.Fit(trainingData);
            _predictionEngine = _mlContext.Model.CreatePredictionEngine<ChatData, ChatPrediction>(model);

            using var fs = new FileStream(_settings.TrainedModelPath, FileMode.Create);
            _mlContext.Model.Save(model, trainingData.Schema, fs);

            stopwatch.Stop();
            Console.WriteLine($"TrainModel executed in {stopwatch.ElapsedMilliseconds} ms");
        }

        //public void TrainModel()
        //{
        //    try
        //    {
        //        var stopwatch = Stopwatch.StartNew();

        //        // Load from CSV
        //        var trainingData = _mlContext.Data.LoadFromTextFile<ChatData>(
        //            path: _settings.TraningDataPath,
        //            hasHeader: true,
        //            separatorChar: ',');



        //        //var pipeline = _mlContext.Transforms.Text.FeaturizeText("Features", nameof(ChatData.Text))
        //        //    .Append(_mlContext.Transforms.Conversion.MapValueToKey("Label"))
        //        //    .Append(_mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy())
        //        //    .Append(_mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));
        //        // Pipeline: use Text + Entities + Context to predict Intent


        //        var model = intentPipeline.Fit(trainingData);
        //        _predictionEngine = _mlContext.Model.CreatePredictionEngine<ChatData, ChatPrediction>(model);

        //        // Save trained model
        //        using var fs = new FileStream(_settings.TrainedModelPath, FileMode.Create, FileAccess.Write, FileShare.Write);
        //        _mlContext.Model.Save(model, trainingData.Schema, fs);

        //        stopwatch.Stop();
        //        Console.WriteLine($"TrainModel executed in {stopwatch.ElapsedMilliseconds} ms");
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine("error: " + ex.Message);
        //        throw;
        //    }
        //}

        public async Task<string> GetChatGptResponseAsync(string userInput, string? filePath = null)
        {
            string fullMessage = "Need small summary of this file.";

            if (!string.IsNullOrEmpty(filePath))
            {
                if (!File.Exists(filePath))
                    throw new FileNotFoundException("File not found!");

                try
                {
                    string fileContent = FileTextExtractor.ExtractTextFromFile(filePath);
                   // if fileContent.Length having 20000
                   // it will reduse it to 10000 right
                    if (fileContent.Length > 10000) // Limit content
                    {
                        fileContent = fileContent.Substring(0, 10000) + "... [truncated]";
                    }

                    fullMessage += $"\n\nFile content:\n{fileContent}";
                }
                catch (NotSupportedException ex)
                {
                    fullMessage += $"\n\n[Error extracting content: {ex.Message}]";
                }
            }

            var requestData = new
            {
                model = "gpt-4", // or "gpt-3.5-turbo"
                messages = new[]
                {
            new { role = "user", content = fullMessage }
        },
                temperature = 0.7,
                max_tokens = 2048
            };

            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _settings.ChatGptApiKey);

            var json = JsonSerializer.Serialize(requestData);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("https://api.openai.com/v1/chat/completions", content);
            var result = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"OpenAI API error: {result}");
            }

            using var doc = JsonDocument.Parse(result);
            var message = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return message ?? "No response from ChatGPT.";
        }

        public async Task<string> SummarizeWithStreaming(string filePath)
        {
            var stopwatch = Stopwatch.StartNew();

            using var client = new HttpClient();

            if (!File.Exists(filePath))
                throw new FileNotFoundException("File not found!");

            string fileContent = FileTextExtractor.ExtractTextFromFile(filePath);

            if (fileContent.Length > 10000)
                fileContent = fileContent.Substring(0, 10000) + "... [truncated]";

            var requestBody = new
            {
                model = "deepseek-coder:1.3b",
                prompt = "Give me short Summary  for the following content what actually this content is about in 5 sentance :\n\n" + fileContent,
                stream = true
            };

            var json = JsonSerializer.Serialize(requestBody);

            using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost:11434/api/generate")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(5));

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationTokenSource.Token);

            response.EnsureSuccessStatusCode(); // Add this to throw if status code is not 200 OK

            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            var sb = new StringBuilder();
            while (!reader.EndOfStream)
            {
                var line = await reader.ReadLineAsync();
                if (!string.IsNullOrWhiteSpace(line) && line.StartsWith("{"))
                {
                    var jsonObj = JsonSerializer.Deserialize<JsonElement>(line);
                    if (jsonObj.TryGetProperty("response", out var responseChunk))
                    {
                        sb.Append(responseChunk.GetString());
                    }
                }
            }
            stopwatch.Stop();
            Console.WriteLine($"SummarizeWithStreaming executed in {stopwatch.ElapsedMilliseconds} ms");
            return sb.ToString();
        }




    }
}
