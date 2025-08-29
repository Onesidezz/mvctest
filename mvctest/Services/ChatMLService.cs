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
        private readonly ILuceneInterface _luceneInterface;
        private readonly string Prompt = @"
                     Please provide a clear and concise summary of this document.

                     Instructions:
                     - Write a brief summary in 2-3 sentences about what the document is about and what it contains
                     - Include key details such as: main purpose, important names/entities, dates, amounts, or key topics
                     - Focus on the most important information that would help someone understand the document quickly
                     - Be direct and factual - do not include reasoning, analysis, or explanations of your process
                     - If the document content is unclear or corrupted, clearly state this limitation

                     Document Content:
                     ";


        public ChatMLService(IOptions<AppSettings> options, HttpClient httpClient, IContentManager contentManager, ILuceneInterface luceneInterface)
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
            _luceneInterface = luceneInterface;
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

        // Static HttpClient for DeepSeek streaming (better performance - reuse connections)
        private static readonly HttpClient _deepSeekHttpClient = new HttpClient()
        {
            Timeout = TimeSpan.FromMinutes(5)
        };


        public async Task<string> DeepSeekSummarizeWithStreaming(string filePath, string prompt = "", string usermessage = "")
        {
            var stopwatch = Stopwatch.StartNew();

            string fileContent = "";

            // Optimized content extraction
            if (!string.IsNullOrWhiteSpace(usermessage))
            {
                // Prioritize user message (already processed content)
                fileContent = usermessage;
            }
            else if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            {
                fileContent = FileTextExtractor.ExtractTextFromFile(filePath);
            }
            else
            {
                throw new FileNotFoundException("File not found and user message is empty!");
            }
            // Optimized request body with enhanced settings for better large content handling
            var requestBody = new
            {
                model = "qwen2.5:14b",//gemma:7b //qwen2.5:7b
                prompt = $"{prompt}\n\n{fileContent}",
                stream = true,
                options = new
                {
                    temperature = 0.3, 
                    top_p = 0.9,
                    num_ctx = 32768, // Doubled context window for better comprehension
                    num_predict = 4096, // Allow longer responses
                     num_thread = Environment.ProcessorCount

                }
            };

            var json = JsonSerializer.Serialize(requestBody);

            using var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost:11434/api/generate")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };

            // Extended timeout for large content processing
            var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(15));

            try
            {
                using var response = await _deepSeekHttpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationTokenSource.Token);

                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream);

                var sb = new StringBuilder();
                var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                // Optimized streaming with early termination
                while (!reader.EndOfStream && !cancellationTokenSource.Token.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("{")) continue;

                    try
                    {
                        var jsonObj = JsonSerializer.Deserialize<JsonElement>(line, jsonOptions);
                        
                        // Check for completion
                        if (jsonObj.TryGetProperty("done", out var doneProperty) && doneProperty.GetBoolean())
                        {
                            break; // Stream is complete
                        }

                        if (jsonObj.TryGetProperty("response", out var responseChunk))
                        {
                            var chunk = responseChunk.GetString();
                            if (!string.IsNullOrEmpty(chunk))
                            {
                                sb.Append(chunk);
                                
                                // Increased response length limit for better summaries
                                if (sb.Length > 15000) // Higher limit for comprehensive summaries
                                {
                                    sb.Append(" [Response truncated - summary continues...]");
                                    break;
                                }
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // Skip malformed JSON lines
                        continue;
                    }
                }

                stopwatch.Stop();
                Console.WriteLine($"SummarizeWithStreaming executed in {stopwatch.ElapsedMilliseconds} ms (Content: {fileContent.Length} chars, Response: {sb.Length} chars)");

                return sb.ToString();
            }
            catch (OperationCanceledException ex)
            {
                return "Response timeout - the operation took too long. Please try with a shorter document or query.";
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"HTTP Error in DeepSeekSummarizeWithStreaming: {ex.Message}");
                return "Service temporarily unavailable. Please try again later.";
            }
        }

        // Fast chunking method for moderately large content (optimized for speed)

        // Intelligent sampling method for very large content (fastest approach)

        private List<string> CreateFastChunks(string content, int chunkSize, int overlapSize, int maxChunks)
        {
            var chunks = new List<string>();
            
            // Simple character-based chunking for speed
            for (int i = 0; i < content.Length && chunks.Count < maxChunks; i += chunkSize - overlapSize)
            {
                var length = Math.Min(chunkSize, content.Length - i);
                chunks.Add(content.Substring(i, length));
            }
            
            return chunks;
        }

        public async Task<string> GetFileSummaryAsync(string filePath)
        {
            var rawResponse = await DeepSeekSummarizeWithStreaming(filePath, Prompt);
            return CleanDeepSeekResponse(rawResponse);
        }

        private string CleanDeepSeekResponse(string rawResponse)
        {
            if (string.IsNullOrEmpty(rawResponse))
                return "No summary available.";

            // Split response into sentences
            var sentences = rawResponse.Split(new char[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                                     .Select(s => s.Trim())
                                     .Where(s => !string.IsNullOrEmpty(s))
                                     .ToList();

            if (sentences.Count == 0)
                return rawResponse;

            // Look for sentences that don't contain reasoning phrases
            var reasoningPhrases = new string[]
            {
                "I need to", "I'll identify", "I should", "First,", "Then,", "Next,", 
                "Maybe", "It might", "I'm supposed to", "Let me", "since it's",
                "probably includes", "It seems to be", "The main points are",
                "because that's not", "Just focus on"
            };

            var cleanSentences = sentences.Where(sentence => 
                !reasoningPhrases.Any(phrase => sentence.Contains(phrase, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            // If we have clean sentences, take the last 2-3 as they're usually the actual summary
            if (cleanSentences.Count >= 2)
            {
                var summaryCount = Math.Min(3, cleanSentences.Count);
                var summary = string.Join(". ", cleanSentences.TakeLast(summaryCount)) + ".";
                return summary;
            }

            // Fallback: take the last 2-3 sentences from original response
            var fallbackCount = Math.Min(3, sentences.Count);
            return string.Join(". ", sentences.TakeLast(fallbackCount)) + ".";
        }

        public async Task<string> DeepContentSearchAsync(string query, List<string> filePaths)
        {
            try
            {
                // Use local semantic search from LuceneInterface
                var semanticResults = _luceneInterface?.SemanticSearch(query, filePaths, 5);
                
                var contentBuilder = new StringBuilder();
                contentBuilder.AppendLine($"User Question: {query}");
                
                if (semanticResults?.Any() == true)
                {
                    contentBuilder.AppendLine("\nRelevant Document Contents (Semantically Matched):");
                    
                    foreach (var result in semanticResults)
                    {
                        contentBuilder.AppendLine($"\n--- File: {result.FileName} (Relevance: {result.Score:F3}) ---");
                        
                        // Use relevant text from metadata if available
                        var relevantText = result.Metadata?.GetValueOrDefault("RelevantText", "");
                        
                        if (!string.IsNullOrEmpty(relevantText))
                        {
                            contentBuilder.AppendLine(relevantText);
                        }
                        else if (File.Exists(result.FilePath))
                        {
                            try
                            {
                                var fileContent = FileTextExtractor.ExtractTextFromFile(result.FilePath);
                                if (fileContent.Length > 3000)
                                {
                                    fileContent = fileContent.Substring(0, 3000) + "... [truncated]";
                                }
                                contentBuilder.AppendLine(fileContent);
                            }
                            catch (Exception ex)
                            {
                                contentBuilder.AppendLine($"[Error reading file: {ex.Message}]");
                            }
                        }
                        
                        // Add metadata context
                        if (result.Metadata?.Any() == true)
                        {
                            var customerInfo = result.Metadata.GetValueOrDefault("CustomerName", "");
                            var customerId = result.Metadata.GetValueOrDefault("CustomerID", "");
                            var invoiceNumber = result.Metadata.GetValueOrDefault("InvoiceNumber", "");
                            
                            if (!string.IsNullOrEmpty(customerInfo) || !string.IsNullOrEmpty(customerId))
                            {
                                contentBuilder.AppendLine($"[Context: Customer: {customerInfo} (ID: {customerId}), Invoice: {invoiceNumber}]");
                            }
                        }
                    }
                }
                else
                {
                    // Fallback to processing specified files directly
                    contentBuilder.AppendLine("\nDocument Contents (Direct File Processing):");
                    
                    foreach (var filePath in filePaths.Take(3)) // Limit to avoid overwhelming the AI
                    {
                        try
                        {
                            if (File.Exists(filePath))
                            {
                                var fileContent = FileTextExtractor.ExtractTextFromFile(filePath);
                                if (fileContent.Length > 10000)
                                {
                                    fileContent = fileContent.Substring(0, 10000) + "... [truncated]";
                                }
                                
                                var fileName = Path.GetFileName(filePath);
                                contentBuilder.AppendLine($"\n--- File: {fileName} ---");
                                contentBuilder.AppendLine(fileContent);
                            }
                        }
                        catch (Exception ex)
                        {
                            var fileName = Path.GetFileName(filePath);
                            contentBuilder.AppendLine($"\n--- File: {fileName} ---");
                            contentBuilder.AppendLine($"[Error reading file: {ex.Message}]");
                        }
                    }
                }

                var deepSearchPrompt = "Answer the user's question based on the provided documents. " +
                                     "Focus on the most relevant information from the semantically matched content. " +
                                     "If the answer is found in specific files, mention which file(s) contain the information. " +
                                     "If the information is not found, say so clearly. " +
                                     "Be specific and provide context from the documents:\n\n";

                var combinedContent = deepSearchPrompt + contentBuilder.ToString();
                
                // Use DeepSeek to analyze the semantically relevant content
                var rawResponse = await DeepSeekSummarizeWithStreaming("", "", combinedContent);
                return CleanDeepSeekResponse(rawResponse);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in DeepContentSearchAsync: {ex.Message}");
                // Fallback to original implementation
                return await FallbackDeepContentSearchAsync(query, filePaths);
            }
        }

        private async Task<string> FallbackDeepContentSearchAsync(string query, List<string> filePaths)
        {
            var contentBuilder = new StringBuilder();
            contentBuilder.AppendLine($"User Question: {query}");
            contentBuilder.AppendLine("\nDocument Contents:");

            // Extract content from all files (fallback method)
            foreach (var filePath in filePaths.Take(3)) // Limit to avoid overwhelming
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        var fileContent = FileTextExtractor.ExtractTextFromFile(filePath);
                        if (fileContent.Length > 10000)
                        {
                            fileContent = fileContent.Substring(0, 10000) + "... [truncated]";
                        }
                        
                        var fileName = Path.GetFileName(filePath);
                        contentBuilder.AppendLine($"\n--- File: {fileName} ---");
                        contentBuilder.AppendLine(fileContent);
                    }
                }
                catch (Exception ex)
                {
                    var fileName = Path.GetFileName(filePath);
                    contentBuilder.AppendLine($"\n--- File: {fileName} ---");
                    contentBuilder.AppendLine($"[Error reading file: {ex.Message}]");
                }
            }

            var deepSearchPrompt = "Answer the user's question based on the provided documents. " +
                                 "If the answer is found in specific files, mention which file(s) contain the information. " +
                                 "If the information is not found in any of the documents, say so clearly. " +
                                 "Be specific and cite the relevant files when possible:\n\n";

            var combinedContent = deepSearchPrompt + contentBuilder.ToString();
            
            // Use DeepSeek to analyze all content and answer the question
            var rawResponse = await DeepSeekSummarizeWithStreaming("", "", combinedContent);
            return CleanDeepSeekResponse(rawResponse);
        }
    }
}
