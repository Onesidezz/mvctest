using mvctest.Services;
using System.Text;
using System.Text.Json;

namespace mvctest.Controllers
{
    public partial class ContentManagerController
    {
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
                string generativeAnswer = null;
                generativeAnswer = await CallHuggingFaceAPI(analysisPrompt);

                if (!string.IsNullOrEmpty(generativeAnswer))
                {
                    Console.WriteLine($"✓ Successfully generated answer from {System.IO.Path.GetFileName(filePath)}");
                    return generativeAnswer.Trim();
                }
                else
                {
                    generativeAnswer = await CallGemmaModel(analysisPrompt);
                }

                return $"Based on the file content: {generativeAnswer}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating answer from {filePath}: {ex.Message}");

                return $"Unable to analyze content from {System.IO.Path.GetFileName(filePath)}";
            }
        }

        private async Task<string> CallHuggingFaceAPI(string prompt)
        {
            try
            {
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("Authorization", _appSettings.HuggingFaceAccessToken);

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
                    model = "openai/gpt-oss-120b",
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

        private bool IsNegativeAnswer(string answer)
        {
            if (string.IsNullOrWhiteSpace(answer))
                return true;

            var lowerAnswer = answer.ToLowerInvariant();
            
            // AI models often use these patterns when they can't find relevant information
            var negativePatterns = new[]
            {
                // Direct statements of unavailability
                "information is not available",
                "not available in the content",
                "information is not provided",
                "not provided in the content",
                "no information",
                "no relevant information",
                "no specific information",
                "no details",
                "no mention",
                
                // AI-style responses indicating lack of information
                "i cannot find",
                "i don't see",
                "i cannot determine",
                "cannot be determined",
                "unable to find",
                "unable to determine",
                "unable to locate",
                "unable to identify",
                "not able to find",
                "not able to determine",
                
                // Content-based negative responses
                "content does not contain",
                "document does not contain",
                "text does not contain",
                "content does not provide",
                "document does not provide",
                "content doesn't mention",
                "document doesn't mention",
                "content doesn't include",
                "doesn't appear to contain",
                "does not appear to contain",
                
                // AI uncertainty expressions
                "based on the content, i cannot",
                "from the provided content, i cannot",
                "the provided information does not",
                "the given content does not",
                "there is no information",
                "there isn't any information",
                "there are no details",
                "there is no mention",
                "no such information",
                
                // Error-related responses
                "unable to analyze",
                "cannot analyze",
                "insufficient information",
                "not enough information",
                "unclear from the content",
                "not clear from the content",
                
                // Common AI hedge phrases for negative responses
                "i'm sorry, but",
                "unfortunately,",
                "regrettably,",
                "it appears that",
                "it seems that there is no",
                "it doesn't seem",
                
                // File-specific negative patterns
                "the file does not",
                "this file does not",
                "the document does not",
                "this document does not",
                
                // Specific patterns for name/entity searches (like your example)
                "does not appear in",
                "do not appear in",
                "is not found in",
                "are not found in",
                "is not mentioned in",
                "are not mentioned in",
                "is not present in",
                "are not present in",
                "does not exist in",
                "do not exist in"
            };

            // Check if the answer contains any negative patterns
            if (negativePatterns.Any(pattern => lowerAnswer.Contains(pattern)))
                return true;
            
            // Enhanced pattern matching for formal AI responses like your example
            // Pattern: "The [entity] does **not** [verb] in [location]"
            if (System.Text.RegularExpressions.Regex.IsMatch(lowerAnswer, 
                @"(the\s+.+?\s+(does\s+)?\*\*not\*\*|does\s+\*\*not\*\*\s+appear|do\s+\*\*not\*\*\s+appear)", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return true;
            
            // Pattern for responses that mention specific entities but say they're not found
            if (System.Text.RegularExpressions.Regex.IsMatch(lowerAnswer, 
                @"(name|term|word|entity|person|item).+?(does\s+not|do\s+not|doesn't|don't).+?(appear|exist|found|mentioned|present)", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return true;
                
            // Additional check: if the answer is very short and seems unhelpful
            if (answer.Trim().Length < 20 && (lowerAnswer.Contains("no") || lowerAnswer.Contains("not") || lowerAnswer.Contains("cannot") || lowerAnswer.Contains("unable")))
                return true;
            
            // Check for responses that only restate the question without answering
            if (lowerAnswer.StartsWith("based on the content") && lowerAnswer.Length < 50)
                return true;
                
            return false;
        }

        private async Task<string> CallGemmaModel(string prompt)
        {
            try
            {
                using var httpClient = new HttpClient();

                var requestBody = new
                {
                    model = "gemma:7b",
                    prompt = prompt,
                    stream = false
                };

                var jsonContent = JsonSerializer.Serialize(requestBody);
                var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                Console.WriteLine("Calling Ollama Gemma API...");
                var response = await httpClient.PostAsync("http://localhost:11434/api/generate", httpContent);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var jsonResponse = JsonSerializer.Deserialize<JsonElement>(responseContent);

                    // Extract the response content from Ollama API response format
                    if (jsonResponse.TryGetProperty("response", out var responseText))
                    {
                        var result = responseText.GetString();
                        Console.WriteLine("✓ Successfully received response from Ollama Gemma API");
                        return result ?? "";
                    }

                    Console.WriteLine("⚠️ Unexpected response format from Ollama API");
                    return "";
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"✗ Ollama API error: {response.StatusCode} - {errorContent}");
                    return "";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ Error calling Ollama Gemma API: {ex.Message}");
                return "";
            }
        }

        private async Task<List<(string filePath, float similarity, List<string> relevantChunks, string documentType)>> PerformSemanticPreFiltering(string query, List<string> filePaths)
        {
            var semanticMatches = new List<(string filePath, float similarity, List<string> relevantChunks, string documentType)>();

            try
            {
                Console.WriteLine($"Starting semantic pre-filtering for query: '{query}' across {filePaths.Count} files");

                var modelPath = System.IO.Path.Combine("C:\\Users\\ukhan2\\Desktop\\ONNXModel", "model.onnx");
                if (!System.IO.File.Exists(modelPath))
                {
                    Console.WriteLine("ONNX model not found, falling back to keyword-based filtering");
                    return await PerformKeywordPreFiltering(query, filePaths);
                }

                using var embeddingService = new TextEmbeddingService(modelPath);
                var queryEmbedding = embeddingService?.GetEmbedding(query);
                if (queryEmbedding == null)
                {
                    Console.WriteLine("Failed to generate query embedding, falling back to keyword filtering");
                    return await PerformKeywordPreFiltering(query, filePaths);
                }

                // Debug: Check if query embedding is valid
                var queryEmbeddingSum = queryEmbedding.Sum();
                Console.WriteLine($"🔍 Query: '{query}' | Embedding sum: {queryEmbeddingSum:F3} | Length: {queryEmbedding.Length}");

                // Process files in batches for better performance
                var batchSize = 5;
                var batches = filePaths.Select((path, index) => new { path, index })
                                      .GroupBy(x => x.index / batchSize)
                                      .Select(g => g.Select(x => x.path).ToList())
                                      .ToList();

                foreach (var batch in batches)
                {
                    Console.WriteLine($"Processing batch of {batch.Count} files");

                    var batchTasks = batch.Select(async filePath =>
                    {
                        return await ProcessFileForSemanticMatch(filePath, query, queryEmbedding, embeddingService);
                    });

                    var batchResults = await Task.WhenAll(batchTasks);
                    semanticMatches.AddRange(batchResults.Where(result => result.similarity > 0));
                }

                // Sort by similarity and return top results
                Console.WriteLine($"Before filtering: {semanticMatches.Count} matches found");
                foreach (var match in semanticMatches.Take(5))
                {
                    Console.WriteLine($"File: {System.IO.Path.GetFileName(match.filePath)}, Similarity: {match.similarity:F4}");
                }
                
                semanticMatches = semanticMatches.OrderByDescending(m => m.similarity)
                                                .Where(m => m.similarity > 0.03f) // Lowered threshold for exact name searches
                                                .ToList();

                Console.WriteLine($"Semantic pre-filtering completed: {semanticMatches.Count} relevant files found");
                return semanticMatches;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in semantic pre-filtering: {ex.Message}");
                return await PerformKeywordPreFiltering(query, filePaths);
            }
        }

        private async Task<(string filePath, float similarity, List<string> relevantChunks, string documentType)> ProcessFileForSemanticMatch(
            string filePath, string query, float[] queryEmbedding, TextEmbeddingService embeddingService)
        {
            try
            {
                if (!System.IO.File.Exists(filePath))
                {
                    return (filePath, 0f, new List<string>(), "not_found");
                }

                Console.WriteLine($"  Processing {System.IO.Path.GetFileName(filePath)} for semantic matching");

                var content = Services.FileTextExtractor.ExtractTextFromFile(filePath);
                if (string.IsNullOrEmpty(content))
                {
                    return (filePath, 0f, new List<string>(), "no_content");
                }

                // Quick semantic check using document summary (first 1000 chars)
                var summary = content.Length > 1000 ? content.Substring(0, 1000) : content;
                var summaryEmbedding = embeddingService?.GetEmbedding(summary);

                if (summaryEmbedding == null)
                {
                    return (filePath, 0f, new List<string>(), "embedding_failed");
                }

                // Debug: Check embedding diversity
                var embeddingSum = summaryEmbedding.Sum();
                var embeddingVariance = summaryEmbedding.Select(x => (x - embeddingSum / summaryEmbedding.Length)).Select(x => x * x).Average();
                Console.WriteLine($"    🧠 {System.IO.Path.GetFileName(filePath)}: embSum={embeddingSum:F3}, variance={embeddingVariance:F6}");

                var docSimilarity = TextEmbeddingService.CosineSimilarity(queryEmbedding, summaryEmbedding);

                // Content-aware similarity boosting for specific terms
                float contentBoost = CalculateContentBoost(query, content);
                float boostedSimilarity = Math.Min(1.0f, docSimilarity + contentBoost);

                // Debug logging to understand the over-boosting issue
                Console.WriteLine($"    📊 {System.IO.Path.GetFileName(filePath)}: docSim={docSimilarity:F3}, boost=+{contentBoost:F3}, final={boostedSimilarity:F3}");

                // If document summary shows promise, check content blocks
                var relevantChunks = new List<string>();
                float maxSimilarity = boostedSimilarity;

                if (docSimilarity > 0.1f) // Only check blocks if document shows some relevance
                {
                    var chunks = SplitContentIntoChunks(content, 500); // Smaller chunks for pre-filtering
                    foreach (var chunk in chunks.Take(5)) // Limit to first 5 chunks for speed
                    {
                        if (string.IsNullOrWhiteSpace(chunk)) continue;

                        var chunkEmbedding = embeddingService?.GetEmbedding(chunk);
                        if (chunkEmbedding != null)
                        {
                            var chunkSimilarity = TextEmbeddingService.CosineSimilarity(queryEmbedding, chunkEmbedding);
                            var chunkBoost = CalculateContentBoost(query, chunk);
                            var boostedChunkSimilarity = Math.Min(1.0f, chunkSimilarity + chunkBoost);

                            if (boostedChunkSimilarity > maxSimilarity)
                            {
                                maxSimilarity = boostedChunkSimilarity;
                            }

                            if (boostedChunkSimilarity > 0.2f)
                            {
                                relevantChunks.Add(chunk);
                            }
                        }
                    }
                }

                if (maxSimilarity > 0.15f)
                {
                    var boostInfo = contentBoost > 0 ? $" (boosted +{contentBoost:F3})" : "";
                    Console.WriteLine($"    ✓ {System.IO.Path.GetFileName(filePath)} - similarity: {maxSimilarity:F3}{boostInfo}");
                }

                return (filePath, maxSimilarity, relevantChunks, "semantic_match");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    ✗ Error processing {System.IO.Path.GetFileName(filePath)}: {ex.Message}");
                return (filePath, 0f, new List<string>(), "error");
            }
        }

        private float CalculateContentBoost(string query, string content)
        {
            if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(content))
                return 0f;

            var queryLower = query.ToLower();
            var contentLower = content.ToLower();
            float boost = 0f;

            // Extract potential character names, IDs, or specific terms from query
            var queryWords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            foreach (var word in queryWords)
            {
                var cleanWord = word.Trim('.', ',', ':', ';', '!', '?', '"', '\'', '(', ')');

                // Boost for exact matches of specific terms
                if (cleanWord.Length > 3 && contentLower.Contains(cleanWord.ToLower()))
                {
                    // Higher boost for character names/IDs (contains hyphens and numbers)
                    if (cleanWord.Contains('-') && cleanWord.Any(char.IsDigit))
                    {
                        boost += 0.08f; // Reduced: Strong boost for character IDs
                    }
                    // Boost for capitalized names
                    else if (char.IsUpper(cleanWord[0]) && cleanWord.Length > 4)
                    {
                        boost += 0.05f; // Reduced: Boost for proper names
                    }
                    // General term match
                    else if (cleanWord.Length > 5)
                    {
                        boost += 0.03f; // Reduced: Moderate boost for specific terms
                    }
                }
            }

            // Additional boost for role-related keywords when asking about character roles
            if (queryLower.Contains("role") || queryLower.Contains("character") || queryLower.Contains("what is") || queryLower.Contains("document name"))
            {
                var roleKeywords = new[] { "merchant", "researcher", "naturalist", "gallant", "intelligent", "analytical" };
                foreach (var keyword in roleKeywords)
                {
                    if (contentLower.Contains(keyword))
                    {
                        boost += 0.02f; // Reduced boost
                    }
                }
            }

            return Math.Min(0.15f, boost); // Reduced cap: prevent over-boosting
        }

        private async Task<List<(string filePath, float similarity, List<string> relevantChunks, string documentType)>> PerformKeywordPreFiltering(string query, List<string> filePaths)
        {
            var keywordMatches = new List<(string filePath, float similarity, List<string> relevantChunks, string documentType)>();

            Console.WriteLine("Performing keyword-based pre-filtering as fallback");

            var queryWords = query.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            foreach (var filePath in filePaths)
            {
                try
                {
                    if (!System.IO.File.Exists(filePath)) continue;

                    var content = Services.FileTextExtractor.ExtractTextFromFile(filePath);
                    if (string.IsNullOrEmpty(content)) continue;

                    var contentLower = content.ToLower();
                    var matches = queryWords.Count(word => contentLower.Contains(word));
                    var similarity = queryWords.Length > 0 ? (float)matches / queryWords.Length : 0f;

                    if (similarity > 0.2f) // At least 20% of query words match
                    {
                        var relevantChunks = ExtractRelevantChunksForKeywords(content, queryWords, 3);
                        keywordMatches.Add((filePath, similarity, relevantChunks, "keyword_match"));
                        Console.WriteLine($"  ✓ {System.IO.Path.GetFileName(filePath)} - keyword similarity: {similarity:F3}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ✗ Error processing {System.IO.Path.GetFileName(filePath)}: {ex.Message}");
                }
            }

            return keywordMatches.OrderByDescending(m => m.similarity).ToList();
        }

        private List<string> ExtractRelevantChunksForKeywords(string content, string[] queryWords, int maxChunks)
        {
            var relevantChunks = new List<string>();
            var sentences = content.Split('.', StringSplitOptions.RemoveEmptyEntries);

            foreach (var sentence in sentences)
            {
                var sentenceLower = sentence.ToLower();
                var matchCount = queryWords.Count(word => sentenceLower.Contains(word));

                if (matchCount > 0 && sentence.Trim().Length > 30)
                {
                    relevantChunks.Add(sentence.Trim());
                    if (relevantChunks.Count >= maxChunks) break;
                }
            }

            return relevantChunks;
        }

        private string? ExtractFilenameFromQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return null;

            // Common file extensions to look for
            var fileExtensions = new[] { ".docx", ".doc", ".pdf", ".pptx", ".ppt", ".xlsx", ".xls", ".txt", ".rtf" };

            // Split query into words and look for filename patterns
            var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            foreach (var word in words)
            {
                var cleanWord = word.Trim('.', ',', ':', ';', '!', '?', '"', '\'', '(', ')');

                // Check if word contains a file extension
                foreach (var extension in fileExtensions)
                {
                    if (cleanWord.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                    {
                        // Additional validation: filename should have reasonable length and structure
                        if (cleanWord.Length > extension.Length + 1 &&
                            cleanWord.Any(c => char.IsLetterOrDigit(c) || c == '_' || c == '-'))
                        {
                            Console.WriteLine($"🔍 Extracted filename from query: '{cleanWord}'");
                            return cleanWord;
                        }
                    }
                }
            }

            // Alternative approach: look for patterns like "document_001361.docx" even if split across words
            var queryLower = query.ToLower();
            foreach (var extension in fileExtensions)
            {
                var extensionPattern = extension.ToLower();
                var extensionIndex = queryLower.IndexOf(extensionPattern);
                if (extensionIndex > 0)
                {
                    // Look backwards to find the start of the filename
                    var startIndex = extensionIndex;
                    while (startIndex > 0 &&
                           (char.IsLetterOrDigit(query[startIndex - 1]) ||
                            query[startIndex - 1] == '_' ||
                            query[startIndex - 1] == '-' ||
                            query[startIndex - 1] == '.'))
                    {
                        startIndex--;
                    }

                    var potentialFilename = query.Substring(startIndex, extensionIndex - startIndex + extension.Length);
                    if (potentialFilename.Length > extension.Length + 1)
                    {
                        Console.WriteLine($"🔍 Extracted filename using pattern matching: '{potentialFilename}'");
                        return potentialFilename;
                    }
                }
            }

            return null;
        }
    }
}