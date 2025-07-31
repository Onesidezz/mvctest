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

      
        
        private readonly MLContext _mlContext;
        private PredictionEngine<ChatData, ChatPrediction> _predictionEngine;
        private readonly AppSettings _settings;
        private readonly HttpClient _httpClient;
        private readonly IContentManager _contentManager;
        private readonly string Prompt = "Summarize the following technical document by explaining only its main purpose and key functions. Avoid any step-by-step analysis or personal commentary. Focus purely on what the document is about and its intended use, in 3-5 concise sentences:\n\n";


        public ChatMLService(IOptions<AppSettings> options, HttpClient httpClient, IContentManager contentManager)
        {
            _settings = options.Value;
            _httpClient = httpClient;
            _mlContext = new MLContext();
            if (File.Exists(_settings.TrainedModelPath))
            {
                using var stream = new FileStream(_settings.TrainedModelPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var model = _mlContext.Model.Load(stream, out var schema);
                _predictionEngine = _mlContext.Model.CreatePredictionEngine<ChatData, ChatPrediction>(model);
            }

            _httpClient = httpClient;
            _contentManager = contentManager;
        }

        public async Task<string> GetChatBotResponse(string userMessage, bool isFromGPT = false, bool isFromDeepseek = false)
        {
            try
            {
                string[] triggers =
                {
            "Do you want a summary of this record? Please Enter Record Name :",
            "Which record summary do you want to know? Please Enter Record Name :",
            "Show me the record summary for Record Name :"
        };

                foreach (var trigger in triggers)
                {
                    if (userMessage.StartsWith(trigger, StringComparison.OrdinalIgnoreCase))
                    {
                        string recordName = userMessage.Substring(trigger.Length).Trim();
                        var record = _contentManager.GetRecordByTitle(recordName);

                        if (record != null)
                        {
                            if (isFromGPT)
                            {
                                var response = await GetChatGptResponseAsync(userMessage, record.ESource, Prompt);
                                return response;
                            }
                            else if (isFromDeepseek)
                            {
                                var summary = await DeepSeekSummarizeWithStreaming(record.ESource, Prompt);
                                return summary;
                            }
                            else
                            {
                                return "Please specify a valid source (isFromGPT or isFromDeepseek).";
                            }
                        }
                        else
                        {
                            return $"Sorry, I couldn't find a record named \"{recordName}\".";
                        }
                    }
                }
                if (isFromGPT)
                {
                    var response = await GetChatGptResponseAsync(userMessage);
                    return response;
                }
                else if (isFromDeepseek)
                {
                    var summary = await DeepSeekSummarizeWithStreaming("","",userMessage);
                    return summary;
                }
                // Fallback to ML.NET intent prediction
                var input = new ChatData { Text = userMessage };
                var prediction = _predictionEngine.Predict(input);
                return prediction.PredictedIntent;
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }


        public async Task TrainModelAsync()
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

            // Save the model
            using var fs = new FileStream(_settings.TrainedModelPath, FileMode.Create);
            _mlContext.Model.Save(model, trainingData.Schema, fs);

            stopwatch.Stop();
            Console.WriteLine($"TrainModel executed in {stopwatch.Elapsed.TotalMinutes:F2} minutes");
        }


        public async Task<string> GetChatGptResponseAsync(string userInput, string? filePath = null,string prompt = "")
        {

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

                    prompt += $"\n\nFile content:\n{fileContent}";
                }
                catch (NotSupportedException ex)
                {
                    prompt += $"\n\n[Error extracting content: {ex.Message}]";
                }
            }

            var requestData = new
            {
                model = "gpt-4o", 
                messages = new[]
                {
                new { role = "user", content = prompt + userInput }
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

        public async Task<string> DeepSeekSummarizeWithStreaming(string filePath, string prompt = "", string usermessage = "")
        {
            var stopwatch = Stopwatch.StartNew();

            using var client = new HttpClient();

            string fileContent = "";

            // If file exists, extract text
            if (File.Exists(filePath))
            {
                fileContent = FileTextExtractor.ExtractTextFromFile(filePath);

                if (fileContent.Length > 100000)
                    fileContent = fileContent.Substring(0, 100000) + "... [truncated]";
            }
            else if (!string.IsNullOrWhiteSpace(usermessage))
            {
                // If file not found but usermessage is available, use it instead
                fileContent = usermessage;
            }
            else
            {
                // If both file and usermessage are empty, throw error
                throw new FileNotFoundException("File not found and user message is empty!");
            }

            var requestBody = new
            {
                model = "deepseek-r1:1.5b",
                prompt = prompt + fileContent,
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

            response.EnsureSuccessStatusCode(); // Will throw if status code != 200 OK

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
