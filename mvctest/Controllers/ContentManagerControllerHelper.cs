using mvctest.Services;
using System.Text;
using System.Text.Json;
using System.Net.Http;
using mvctest.Models;
using Microsoft.Extensions.Options;

namespace mvctest.Controllers
{
    public partial class ContentManagerController
    {
        private readonly AppSettings _appSettings;
        public ContentManagerController(IOptions< AppSettings> appSettings)
        {
            _appSettings = appSettings.Value;
        }
        private async Task<float> CalculateContentRelevance(string query, string content)
        {
            try
            {
                var modelPath = System.IO.Path.Combine("C:\\Users\\ukhan2\\Desktop\\ONNXModel", "model.onnx");
                if (!System.IO.File.Exists(modelPath))
                {
                    return CalculateKeywordRelevance(query, content);
                }

                using var embeddingService = new TextEmbeddingService(modelPath);
                var queryEmbedding = embeddingService?.GetEmbedding(query);
                if (queryEmbedding == null) return 0f;
                
                var chunks = SplitContentIntoChunks(content, 1000);
                float maxSimilarity = 0f;
                
                foreach (var chunk in chunks)
                {
                    if (string.IsNullOrWhiteSpace(chunk)) continue;
                    var chunkEmbedding = embeddingService?.GetEmbedding(chunk);
                    if (chunkEmbedding != null)
                    {
                        var similarity = TextEmbeddingService.CosineSimilarity(queryEmbedding, chunkEmbedding);
                        maxSimilarity = Math.Max(maxSimilarity, similarity);
                    }
                }
                return maxSimilarity;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calculating content relevance: {ex.Message}");
                return CalculateKeywordRelevance(query, content);
            }
        }

        private float CalculateKeywordRelevance(string query, string content)
        {
            if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(content)) return 0f;
            
            var queryWords = query.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var contentLower = content.ToLower();
            
            int matches = 0;
            foreach (var word in queryWords)
            {
                if (contentLower.Contains(word)) matches++;
            }
            
            return queryWords.Length > 0 ? (float)matches / queryWords.Length : 0f;
        }

        private List<string> SplitContentIntoChunks(string content, int chunkSize)
        {
            var chunks = new List<string>();
            if (string.IsNullOrEmpty(content)) return chunks;
            
            for (int i = 0; i < content.Length; i += chunkSize)
            {
                var length = Math.Min(chunkSize, content.Length - i);
                chunks.Add(content.Substring(i, length));
            }
            return chunks;
        }

        private async Task<string> GetGenerativeAnswers(string query, string filePath, string content)
        {
            try
            {
                Console.WriteLine($"Generating AI answer for query: '{query}' from file: {System.IO.Path.GetFileName(filePath)}");
                
                var analysisPrompt = $@"
                        Based on the content from the file '{System.IO.Path.GetFileName(filePath)}', please analyze and answer the following question:

                        Question: {query}

                        File Content:
                        {content}

                        Instructions:
                        - Provide a direct, specific answer based on the content above
                        - If the information is not available in the content, clearly state that
                        - Focus on being accurate and relevant to the question
                        - Extract specific facts, numbers, or details that answer the question
                        - Keep the response concise but informative

Answer:";

                var generativeAnswer = await CallHuggingFaceAPI(analysisPrompt);
                
                if (!string.IsNullOrEmpty(generativeAnswer))
                {
                    Console.WriteLine($"✓ Successfully generated answer from {System.IO.Path.GetFileName(filePath)}");
                    return generativeAnswer.Trim();
                }
                
                var relevantSnippet = ExtractRelevantSnippet(query, content, 300);
                return $"Based on the file content: {relevantSnippet}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating answer from {filePath}: {ex.Message}");
                try
                {
                    var fallbackSnippet = ExtractRelevantSnippet(query, content, 200);
                    return $"Content from {System.IO.Path.GetFileName(filePath)}: {fallbackSnippet}";
                }
                catch
                {
                    return $"Unable to analyze content from {System.IO.Path.GetFileName(filePath)}";
                }
            }
        }

        private string ExtractRelevantSnippet(string query, string content, int maxLength)
        {
            if (string.IsNullOrEmpty(content)) return "No content available";
            
            var queryWords = query.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var contentLower = content.ToLower();
            
            int bestIndex = -1;
            foreach (var word in queryWords)
            {
                int index = contentLower.IndexOf(word);
                if (index != -1 && (bestIndex == -1 || index < bestIndex))
                {
                    bestIndex = index;
                }
            }
            
            if (bestIndex == -1)
            {
                return content.Length <= maxLength ? content : content.Substring(0, maxLength) + "...";
            }
            
            int start = Math.Max(0, bestIndex - maxLength / 3);
            int length = Math.Min(maxLength, content.Length - start);
            
            var snippet = content.Substring(start, length);
            
            if (start > 0) snippet = "..." + snippet;
            if (start + length < content.Length) snippet += "...";
            
            return snippet;
        }

        private async Task<string> CallHuggingFaceAPI(string prompt)
        {
            try
            {
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("Authorization",_appSettings.HuggingFaceAccessToken);
                
                var requestBody = new
                {
                    messages = new[]
                    {
                        new
                        {
                            role = "user",
                            content = prompt
                        }
                    },
                    model = "openai/gpt-oss-120b:novita",
                    stream = false
                };

                var jsonContent = JsonSerializer.Serialize(requestBody);
                var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                Console.WriteLine("Calling Hugging Face API...");
                var response = await httpClient.PostAsync("https://router.huggingface.co/v1/chat/completions", httpContent);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);
                    
                    // Extract the response content from the API response
                    if (jsonResponse.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                    {
                        var firstChoice = choices[0];
                        if (firstChoice.TryGetProperty("message", out var message) &&
                            message.TryGetProperty("content", out var content))
                        {
                            var result = content.GetString();
                            Console.WriteLine("✓ Successfully received response from Hugging Face API");
                            return result ?? "";
                        }
                    }
                    
                    Console.WriteLine("⚠️ Unexpected response format from Hugging Face API");
                    return "";
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"✗ Hugging Face API error: {response.StatusCode} - {errorContent}");
                    return "";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Error calling Hugging Face API: {ex.Message}");
                return "";
            }
        }
    }
}