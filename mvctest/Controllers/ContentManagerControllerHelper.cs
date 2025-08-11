using mvctest.Services;
using System.Text;
using System.Text.Json;

namespace mvctest.Controllers
{
    public partial class ContentManagerController
    {
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
                                    Based on the content from the file '{System.IO.Path.GetFileName(filePath)}', please analyze and answer the following question.

                                    Note: The question may be incomplete or vague. Extract the most relevant keywords from the query and compare them against the content to determine the most accurate and relevant answer.

                                    Question: {query}

                                    File Content:
                                    {content}

                                    Instructions:
                                    - Provide a direct, specific answer based only on the content above.
                                    - If the required information is not available in the content, clearly state: 'The requested information is not available in the document.'
                                    - Focus on accuracy, matching terms from the question with the content where possible.
                                    - Extract specific facts, numbers, names, events, or roles that best answer the question.
                                    - Keep the response concise but informative.

                                    Answer:";

                string generativeAnswer = null;
                //generativeAnswer = await CallHuggingFaceAPI(analysisPrompt);
                generativeAnswer = await CallGemmaModel(analysisPrompt);

                if (!string.IsNullOrEmpty(generativeAnswer))
                {
                    Console.WriteLine($"✓ Successfully generated answer from {System.IO.Path.GetFileName(filePath)}");
                    Console.WriteLine($"generativeAnswer: {generativeAnswer}");
                    return generativeAnswer.Trim();
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

        /// <summary>
        /// SMART Early Termination: Stop processing once we get a high-quality answer
        /// Saves unnecessary API calls and improves performance
        /// </summary>
        private async Task<string> GenerateAnswerWithEarlyTermination(string query, List<(string filePath, float relevanceScore, string relevantContent)> detailedResults)
        {
            var processedFiles = 0;
            var maxFiles = Math.Min(5, detailedResults.Count); // Process max 5 files
            
            Console.WriteLine($"🎯 Early Termination Logic: Will process max {maxFiles} files, stopping at first good answer");

            foreach (var (filePath, score, content) in detailedResults.Take(maxFiles))
            {
                processedFiles++;
                
                try
                {
                    Console.WriteLine($"📄 Processing file {processedFiles}/{maxFiles}: {System.IO.Path.GetFileName(filePath)} (score: {score:F3})");

                    var fileAnswer = await GetGenerativeAnswers(query, filePath, content);

                    // 🚀 EARLY TERMINATION CONDITIONS
                    if (!string.IsNullOrEmpty(fileAnswer) && !IsNegativeAnswer(fileAnswer))
                    {
                        var answerQuality = EvaluateAnswerQuality(fileAnswer, query);
                        Console.WriteLine($"📊 Answer quality: {answerQuality:F2} (threshold: 0.7)");
                        
                        // ✅ TERMINATE EARLY if we get a high-quality answer
                        if (answerQuality >= 0.7f || score >= 0.8f)
                        {
                            Console.WriteLine($"🏆 HIGH QUALITY ANSWER FOUND! Terminating early after {processedFiles} files");
                            Console.WriteLine($"⚡ Saved {maxFiles - processedFiles} unnecessary API calls!");
                            return $"From {System.IO.Path.GetFileName(filePath)}: {fileAnswer}";
                        }
                        
                        // 🎯 For exact identifier searches, ANY positive answer terminates
                        if (HasExactIdentifier(query) && score >= 0.9f)
                        {
                            Console.WriteLine($"🔍 EXACT MATCH with positive answer! Terminating early");
                            return $"From {System.IO.Path.GetFileName(filePath)}: {fileAnswer}";
                        }
                        
                        // Store as backup answer but continue searching
                        if (processedFiles == 1) // First valid answer becomes fallback
                        {
                            Console.WriteLine($"💾 Stored first valid answer as backup");
                            var firstValidAnswer = $"From {System.IO.Path.GetFileName(filePath)}: {fileAnswer}";
                            
                            // If this is a good relevance match, use it after trying one more file
                            if (score >= 0.6f && processedFiles < maxFiles)
                            {
                                Console.WriteLine($"🔄 Good answer found, checking one more file for comparison");
                                continue;
                            }
                            else
                            {
                                Console.WriteLine($"✅ Using first valid answer - relevance acceptable");
                                return firstValidAnswer;
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine($"❌ Negative/unhelpful answer from {System.IO.Path.GetFileName(filePath)} - continuing");
                    }
                    
                    // 🛑 SMART BREAK: If first 2 files give negative results and scores are low, stop
                    if (processedFiles >= 2 && score < 0.3f)
                    {
                        Console.WriteLine($"🛑 Low confidence after 2 files - early termination to save resources");
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Error processing {System.IO.Path.GetFileName(filePath)}: {ex.Message}");
                }
            }

            Console.WriteLine($"⚠️ No high-quality answer found after processing {processedFiles} files");
            
            // Fallback: Use content from best file
            var bestFile = detailedResults.First();
            var snippet = bestFile.relevantContent.Length > 500 
                ? bestFile.relevantContent.Substring(0, 500) + "..." 
                : bestFile.relevantContent;
            
            return $"Based on content from {System.IO.Path.GetFileName(bestFile.filePath)}: {snippet}";
        }

        /// <summary>
        /// Evaluate answer quality to determine if we should terminate early
        /// </summary>
        private float EvaluateAnswerQuality(string answer, string query)
        {
            if (string.IsNullOrWhiteSpace(answer))
                return 0f;

            float score = 0f;
            var answerLower = answer.ToLower();
            var queryWords = query.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // ✅ High quality indicators
            if (answer.Length > 50 && answer.Length < 1000) score += 0.3f; // Good length
            if (queryWords.Any(word => answerLower.Contains(word))) score += 0.4f; // Contains query terms
            if (answer.Contains(":")|| answer.Contains("•") || answer.Contains("-")) score += 0.2f; // Structured
            if (!answerLower.Contains("i cannot") && !answerLower.Contains("not available")) score += 0.3f; // Positive
            
            // ✅ Extra points for specific content
            if (answerLower.Contains("guid") || answerLower.Contains("character") || answerLower.Contains("name")) score += 0.2f;
            
            // ❌ Quality penalties
            if (answer.Length < 20) score -= 0.3f; // Too short
            if (answer.Length > 1500) score -= 0.2f; // Too verbose
            if (answerLower.Contains("based on the content") && answer.Length < 100) score -= 0.4f; // Generic response

            return Math.Max(0f, Math.Min(1f, score));
        }

        /// <summary>
        /// Check if query contains exact identifiers (GUIDs, codes, etc.)
        /// </summary>
        private bool HasExactIdentifier(string query)
        {
            var patterns = new[]
            {
                @"[a-fA-F0-9]{8,}(?:-[a-fA-F0-9]{4,})*", // GUIDs
                @"\b[A-Z0-9]{6,}\b", // Codes
                @"\b\d{8,}\b" // Long numbers
            };

            return patterns.Any(pattern => System.Text.RegularExpressions.Regex.IsMatch(query, pattern));
        }

        private async Task<List<(string filePath, float similarity, List<string> relevantChunks, string documentType)>> PerformSemanticPreFiltering(string query, List<string> filePaths)
        {
            try
            {
                Console.WriteLine($"🚀 Starting INTELLIGENT pre-filtering for query: '{query}' across {filePaths.Count} files");

                // Use the new IntelligentSearchOrchestrator for better query analysis and ranking
                var serviceProvider = HttpContext.RequestServices;
                var logger = serviceProvider.GetRequiredService<ILogger<IntelligentSearchOrchestrator>>();
                var orchestrator = new IntelligentSearchOrchestrator(_luceneInterface, logger);
                
                // Perform smart search with proper exact match prioritization
                var results = await orchestrator.SmartSearchAsync(query, filePaths);
                
                Console.WriteLine($"✅ Intelligent search completed: {results.Count} results found");
                foreach (var result in results.Take(5))
                {
                    Console.WriteLine($"  📄 {Path.GetFileName(result.filePath)}: Score={result.similarity:F3}, Type={result.documentType}");
                }
                
                return results;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error in intelligent pre-filtering: {ex.Message}");
                // Fallback to the old hybrid search on error
                return await PerformHybridSearch(query, filePaths);
            }
        }

        private async Task<List<(string filePath, float similarity, List<string> relevantChunks, string documentType)>> PerformHybridSearch(string query, List<string> filePaths)
        {
            Console.WriteLine("🔄 Performing HYBRID search combining semantic and keyword approaches");

            var hybridResults = new Dictionary<string, HybridSearchResult>();

            // Step 1: Perform keyword search
            var keywordResults = await PerformKeywordPreFiltering(query, filePaths);
            foreach (var result in keywordResults)
            {
                if (!hybridResults.ContainsKey(result.filePath))
                {
                    hybridResults[result.filePath] = new HybridSearchResult
                    {
                        FilePath = result.filePath,
                        KeywordScore = result.similarity,
                        RelevantChunks = result.relevantChunks,
                        DocumentType = result.documentType
                    };
                }
            }

            // Step 2: Perform semantic search (if model available)
            var semanticResults = await PerformPureSemanticSearch(query, filePaths);
            foreach (var result in semanticResults)
            {
                if (hybridResults.ContainsKey(result.filePath))
                {
                    hybridResults[result.filePath].SemanticScore = result.similarity;
                    // Merge chunks
                    if (result.relevantChunks.Any())
                    {
                        hybridResults[result.filePath].RelevantChunks.AddRange(result.relevantChunks);
                    }
                }
                else
                {
                    hybridResults[result.filePath] = new HybridSearchResult
                    {
                        FilePath = result.filePath,
                        SemanticScore = result.similarity,
                        RelevantChunks = result.relevantChunks,
                        DocumentType = result.documentType
                    };
                }
            }

            // Step 3: Calculate combined scores
            var finalResults = new List<(string filePath, float similarity, List<string> relevantChunks, string documentType)>();

            foreach (var result in hybridResults.Values)
            {
                // Weighted combination: Give more weight to exact matches
                float combinedScore;

                if (result.DocumentType == "exact_match")
                {
                    // Exact matches get maximum score
                    combinedScore = 1.0f;
                }
                else if (result.KeywordScore > 0 && result.SemanticScore > 0)
                {
                    // Both scores available - weighted average
                    combinedScore = (result.KeywordScore * 0.4f) + (result.SemanticScore * 0.6f);
                }
                else if (result.KeywordScore > 0)
                {
                    // Only keyword score
                    combinedScore = result.KeywordScore * 0.7f; // Slight penalty for no semantic match
                }
                else
                {
                    // Only semantic score
                    combinedScore = result.SemanticScore * 0.9f; // Small penalty for no keyword match
                }

                // Boost for specific patterns
                combinedScore = ApplySmartBoosts(query, result.FilePath, combinedScore);

                finalResults.Add((
                    result.FilePath,
                    Math.Min(1.0f, combinedScore),
                    result.RelevantChunks.Distinct().Take(5).ToList(),
                    result.DocumentType
                ));

                Console.WriteLine($"  📊 {System.IO.Path.GetFileName(result.FilePath)}: " +
                                 $"Keyword={result.KeywordScore:F3}, Semantic={result.SemanticScore:F3}, " +
                                 $"Combined={combinedScore:F3}");
            }

            // Sort by combined score
            return finalResults.OrderByDescending(r => r.similarity).ToList();
        }

        private async Task<List<(string filePath, float similarity, List<string> relevantChunks, string documentType)>> PerformPureSemanticSearch(string query, List<string> filePaths)
        {
            var semanticMatches = new List<(string filePath, float similarity, List<string> relevantChunks, string documentType)>();

            try
            {
                Console.WriteLine($"🧠 Using stored ONNX embeddings for semantic search: '{query}'");
                
                // Use the existing SemanticSearch method that leverages stored embeddings
                var luceneResults = _luceneInterface.SemanticSearch(query, filePaths, 50);
                
                if (luceneResults?.Any() == true)
                {
                    Console.WriteLine($"✅ Found {luceneResults.Count} results from stored embeddings");
                    
                    // Convert Lucene results to the expected format
                    foreach (var result in luceneResults)
                    {
                        var relevantChunks = new List<string>();
                        if (!string.IsNullOrEmpty(result.Content))
                        {
                            // Extract relevant snippets from the content
                            var snippets = _luceneInterface.GetAllContentSnippets(result.Content, query, 300);
                            relevantChunks = snippets.Take(3).ToList();
                        }
                        
                        semanticMatches.Add((
                            result.FilePath ?? result.FileName ?? "",
                            result.Score, // This is the similarity score from cosine similarity
                            relevantChunks,
                            "semantic_match"
                        ));
                        
                        Console.WriteLine($"  📄 {Path.GetFileName(result.FilePath ?? result.FileName)}: Similarity={result.Score:F3}");
                    }
                }
                else
                {
                    Console.WriteLine("⚠️ No results found with stored ONNX embeddings");
                }

                return semanticMatches.OrderByDescending(m => m.similarity).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error in ONNX-based semantic search: {ex.Message}");
                return semanticMatches;
            }
        }

        private async Task<List<(string filePath, float similarity, List<string> relevantChunks, string documentType)>> PerformKeywordPreFiltering(string query, List<string> filePaths)
        {
            var keywordMatches = new List<(string filePath, float similarity, List<string> relevantChunks, string documentType)>();

            Console.WriteLine("Performing keyword-based filtering");

            // Extract different types of search terms
            var exactTerms = ExtractExactSearchTerms(query);
            var importantWords = ExtractImportantWords(query);
            var fuzzyTerms = GenerateFuzzyVariants(importantWords);

            Console.WriteLine($"🎯 Exact terms: {string.Join(", ", exactTerms)}");
            Console.WriteLine($"📝 Important words: {string.Join(", ", importantWords)}");
            Console.WriteLine($"🔍 Fuzzy variants: {string.Join(", ", fuzzyTerms.Take(5))}...");

            foreach (var filePath in filePaths)
            {
                try
                {
                    if (!System.IO.File.Exists(filePath)) continue;

                    var content = Services.FileTextExtractor.ExtractTextFromFile(filePath);
                    if (string.IsNullOrEmpty(content)) continue;

                    var contentLower = content.ToLower();
                    float similarity = 0f;
                    var matchType = "keyword_match";
                    var matchedTerms = new List<string>();

                    // Check for exact matches (highest priority)
                    foreach (var exactTerm in exactTerms)
                    {
                        if (content.Contains(exactTerm, StringComparison.OrdinalIgnoreCase))
                        {
                            similarity = 1.0f;
                            matchType = "exact_match";
                            matchedTerms.Add(exactTerm);
                            Console.WriteLine($"🎯 EXACT MATCH in {System.IO.Path.GetFileName(filePath)}: '{exactTerm}'");
                            break;
                        }
                    }

                    // If no exact match, check important words
                    if (similarity < 1.0f && importantWords.Any())
                    {
                        var wordMatches = 0;
                        foreach (var word in importantWords)
                        {
                            if (contentLower.Contains(word.ToLower()))
                            {
                                wordMatches++;
                                matchedTerms.Add(word);
                            }
                        }

                        if (wordMatches > 0)
                        {
                            similarity = Math.Max(similarity, (float)wordMatches / importantWords.Count);

                            // Boost for multiple word matches
                            if (wordMatches >= 2)
                            {
                                similarity = Math.Min(1.0f, similarity * 1.2f);
                            }
                        }
                    }

                    // Check fuzzy matches (lower priority)
                    if (similarity < 0.5f && fuzzyTerms.Any())
                    {
                        var fuzzyMatches = fuzzyTerms.Count(term => contentLower.Contains(term.ToLower()));
                        if (fuzzyMatches > 0)
                        {
                            var fuzzyScore = (float)fuzzyMatches / fuzzyTerms.Count * 0.7f; // Lower weight for fuzzy
                            similarity = Math.Max(similarity, fuzzyScore);
                            matchType = "fuzzy_match";
                        }
                    }

                    if (similarity > 0.1f)
                    {
                        var relevantChunks = ExtractSmartChunks(content, query, matchedTerms, 5);
                        keywordMatches.Add((filePath, similarity, relevantChunks, matchType));
                        Console.WriteLine($"  ✓ {System.IO.Path.GetFileName(filePath)} - {matchType} (score: {similarity:F3})");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ✗ Error processing {System.IO.Path.GetFileName(filePath)}: {ex.Message}");
                }
            }

            return keywordMatches.OrderByDescending(m => m.similarity).ToList();
        }
        private QueryAnalysis AnalyzeQueryType(string query)
        {
            var analysis = new QueryAnalysis { Query = query };

            if (string.IsNullOrWhiteSpace(query))
            {
                analysis.queryType = QueryType.Natural;
                analysis.confidence = 0.5f;
                return analysis;
            }

            var queryLower = query.ToLower();
            var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // Check for exact search indicators
            var exactIndicators = 0;
            var naturalIndicators = 0;
            var hybridIndicators = 0;

            // Patterns suggesting exact search
            if (System.Text.RegularExpressions.Regex.IsMatch(query, @"[0-9a-fA-F]{8}[-]?[0-9a-fA-F]{4}[-]?[0-9a-fA-F]{4}[-]?[0-9a-fA-F]{4}[-]?[0-9a-fA-F]{12}"))
                exactIndicators += 3; // GUID

            if (System.Text.RegularExpressions.Regex.IsMatch(query, @"\b[A-Z0-9]{6,}\b"))
                exactIndicators += 2; // Code/ID

            if (queryLower.Contains("find") || queryLower.Contains("locate") || queryLower.Contains("which file"))
                exactIndicators += 1;

            if (queryLower.Contains("exact") || queryLower.Contains("specifically"))
                exactIndicators += 2;

            // Patterns suggesting natural language
            if (queryLower.Contains("what") || queryLower.Contains("how") || queryLower.Contains("why") ||
                queryLower.Contains("when") || queryLower.Contains("who") || queryLower.Contains("explain"))
                naturalIndicators += 2;

            if (words.Length > 5)
                naturalIndicators += 1;

            if (queryLower.Contains("tell me about") || queryLower.Contains("describe") || queryLower.Contains("summary"))
                naturalIndicators += 2;

            // Patterns suggesting hybrid approach
            if (queryLower.Contains("about") && words.Any(w => char.IsUpper(w[0]) && w.Length > 2))
                hybridIndicators += 2; // "about John Smith" - name search but natural query

            if (System.Text.RegularExpressions.Regex.IsMatch(query, @"\b[A-Z][a-z]+ [A-Z][a-z]+\b"))
                hybridIndicators += 1; // Proper names

            if (words.Length >= 3 && words.Length <= 5 && !queryLower.StartsWith("what") && !queryLower.StartsWith("how"))
                hybridIndicators += 1; // Medium-length specific queries

            // Determine query type based on indicators
            var maxIndicator = Math.Max(exactIndicators, Math.Max(naturalIndicators, hybridIndicators));

            if (maxIndicator == 0)
            {
                // Default to hybrid for uncertain cases
                analysis.queryType = QueryType.Hybrid;
                analysis.confidence = 0.5f;
            }
            else if (exactIndicators == maxIndicator)
            {
                analysis.queryType = QueryType.ExactSearch;
                analysis.confidence = exactIndicators / 5.0f; // Normalize to 0-1
            }
            else if (naturalIndicators == maxIndicator)
            {
                analysis.queryType = QueryType.Natural;
                analysis.confidence = naturalIndicators / 5.0f;
            }
            else
            {
                analysis.queryType = QueryType.Hybrid;
                analysis.confidence = hybridIndicators / 3.0f;
            }

            analysis.confidence = Math.Min(1.0f, analysis.confidence);

            return analysis;
        }
        private List<string> ExtractImportantWords(string query)
        {
            var importantWords = new List<string>();

            // Remove stop words and extract meaningful terms
            var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "the", "is", "at", "which", "on", "a", "an", "and", "or", "but",
        "in", "with", "to", "for", "of", "as", "from", "by", "about",
        "what", "where", "when", "how", "why", "who", "whom", "whose",
        "this", "that", "these", "those", "there", "here", "file", "document"
    };

            var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            foreach (var word in words)
            {
                var cleanWord = word.Trim('.', ',', '?', '!', ':', ';', '"', '\'', '(', ')');

                // Keep proper nouns (capitalized words)
                if (char.IsUpper(cleanWord[0]) && cleanWord.Length > 2)
                {
                    importantWords.Add(cleanWord);
                }
                // Keep non-stop words that are meaningful
                else if (!stopWords.Contains(cleanWord) && cleanWord.Length > 2)
                {
                    importantWords.Add(cleanWord);
                }
            }

            return importantWords.Distinct().ToList();
        }

        private List<string> GenerateFuzzyVariants(List<string> words)
        {
            var variants = new HashSet<string>();

            foreach (var word in words)
            {
                variants.Add(word.ToLower());
                variants.Add(word.ToUpper());

                // Add common variations
                if (word.Length > 4)
                {
                    // Plural/singular
                    if (word.EndsWith("s"))
                        variants.Add(word.Substring(0, word.Length - 1));
                    else
                        variants.Add(word + "s");

                    // Common suffixes
                    if (word.EndsWith("ing"))
                        variants.Add(word.Substring(0, word.Length - 3));
                    else if (word.EndsWith("ed"))
                        variants.Add(word.Substring(0, word.Length - 2));
                }
            }

            return variants.ToList();
        }

        private List<string> ExtractSmartChunks(string content, string query, List<string> matchedTerms, int maxChunks)
        {
            var chunks = new List<string>();
            var sentences = content.Split(new[] { '.', '!', '?', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            // First, get chunks containing matched terms
            foreach (var term in matchedTerms.Take(3)) // Limit to avoid too many chunks
            {
                foreach (var sentence in sentences)
                {
                    if (sentence.Contains(term, StringComparison.OrdinalIgnoreCase))
                    {
                        chunks.Add(sentence.Trim());
                        if (chunks.Count >= maxChunks) break;
                    }
                }
                if (chunks.Count >= maxChunks) break;
            }

            // If not enough chunks, add sentences with high keyword density
            if (chunks.Count < maxChunks)
            {
                var queryWords = query.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var scoredSentences = sentences
                    .Select(s => new
                    {
                        Sentence = s,
                        Score = queryWords.Count(w => s.ToLower().Contains(w))
                    })
                    .Where(s => s.Score > 0 && !chunks.Contains(s.Sentence))
                    .OrderByDescending(s => s.Score)
                    .Take(maxChunks - chunks.Count);

                chunks.AddRange(scoredSentences.Select(s => s.Sentence.Trim()));
            }

            return chunks.Distinct().Take(maxChunks).ToList();
        }

        private float ApplySmartBoosts(string query, string filePath, float baseScore)
        {
            float boost = 0f;
            var fileName = System.IO.Path.GetFileName(filePath).ToLower();
            var queryLower = query.ToLower();

            // Boost if filename contains important query words
            var importantWords = ExtractImportantWords(query);
            foreach (var word in importantWords)
            {
                if (fileName.Contains(word.ToLower()))
                {
                    boost += 0.05f;
                }
            }

            // Boost for recent files (if applicable)
            try
            {
                var fileInfo = new System.IO.FileInfo(filePath);
                var daysSinceModified = (DateTime.Now - fileInfo.LastWriteTime).TotalDays;
                if (daysSinceModified < 7)
                    boost += 0.03f;
                else if (daysSinceModified < 30)
                    boost += 0.01f;
            }
            catch { }

            return Math.Min(1.0f, baseScore + boost);
        }

        private List<string> ExtractExactSearchTerms(string query)
        {
            var exactTerms = new List<string>();

            // GUID pattern (more flexible)
            var guidPattern = @"[0-9a-fA-F]{8}[-]?[0-9a-fA-F]{4}[-]?[0-9a-fA-F]{4}[-]?[0-9a-fA-F]{4}[-]?[0-9a-fA-F]{12}";
            var guidMatches = System.Text.RegularExpressions.Regex.Matches(query, guidPattern);
            foreach (System.Text.RegularExpressions.Match match in guidMatches)
            {
                exactTerms.Add(match.Value);
                exactTerms.Add(match.Value.ToLower());
                exactTerms.Add(match.Value.ToUpper());
            }

            // Email addresses
            var emailPattern = @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b";
            var emailMatches = System.Text.RegularExpressions.Regex.Matches(query, emailPattern);
            foreach (System.Text.RegularExpressions.Match match in emailMatches)
            {
                exactTerms.Add(match.Value);
            }

            // Phone numbers
            var phonePattern = @"\b\d{3}[-.]?\d{3}[-.]?\d{4}\b";
            var phoneMatches = System.Text.RegularExpressions.Regex.Matches(query, phonePattern);
            foreach (System.Text.RegularExpressions.Match match in phoneMatches)
            {
                exactTerms.Add(match.Value);
            }

            // Other patterns (codes, IDs, etc.)
            var patterns = new[]
            {
        @"\b[A-Z0-9]{6,}\b", // Alphanumeric codes
        @"\b\d{6,}\b",       // Long numbers
        @"\b[A-Z0-9]+-\d+\b" // Hyphenated codes
    };

            foreach (var pattern in patterns)
            {
                var matches = System.Text.RegularExpressions.Regex.Matches(query, pattern);
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    if (!exactTerms.Contains(match.Value))
                    {
                        exactTerms.Add(match.Value);
                    }
                }
            }

            return exactTerms.Distinct().ToList();
        }

        // Helper classes
        private class QueryAnalysis
        {
            public string Query { get; set; }
            public QueryType queryType { get; set; }
            public float confidence { get; set; }
        }

        private enum QueryType
        {
            ExactSearch,  // Looking for specific IDs, codes, GUIDs
            Natural,      // Natural language questions
            Hybrid        // Mix or uncertain
        }

        private class HybridSearchResult
        {
            public string FilePath { get; set; }
            public float KeywordScore { get; set; }
            public float SemanticScore { get; set; }
            public List<string> RelevantChunks { get; set; } = new List<string>();
            public string DocumentType { get; set; }
        }
    }
}