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
        private async Task<List<(string filePath, float similarity, List<string> relevantChunks, string documentType)>> PerformSemanticPreFiltering(string query, List<string> filePaths)
        {
            try
            {
                Console.WriteLine($"Starting intelligent pre-filtering for query: '{query}' across {filePaths.Count} files");

                // Analyze query type
                var queryAnalysis = AnalyzeQueryType(query);
                Console.WriteLine($"Query Analysis - Type: {queryAnalysis.queryType}, Confidence: {queryAnalysis.confidence:F2}");

                // Decide search strategy based on query analysis
                if (queryAnalysis.queryType == QueryType.ExactSearch && queryAnalysis.confidence > 0.7)
                {
                    Console.WriteLine("🎯 Using keyword-based search for exact/identifier query");
                    return await PerformKeywordPreFiltering(query, filePaths);
                }
                else if (queryAnalysis.queryType == QueryType.Hybrid || queryAnalysis.confidence < 0.7)
                {
                    Console.WriteLine("🔄 Using HYBRID search (semantic + keyword) for best results");
                    return await PerformHybridSearch(query, filePaths);
                }
                else
                {
                    Console.WriteLine("🧠 Using semantic search for natural language query");
                    return await PerformPureSemanticSearch(query, filePaths);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in intelligent pre-filtering: {ex.Message}");
                // Fallback to hybrid search on error
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
                var modelPath = System.IO.Path.Combine("C:\\Users\\ukhan2\\Desktop\\ONNXModel", "model.onnx");
                if (!System.IO.File.Exists(modelPath))
                {
                    Console.WriteLine("ONNX model not found for semantic search");
                    return semanticMatches;
                }

                using var embeddingService = new TextEmbeddingService(modelPath);
                var queryEmbedding = embeddingService?.GetEmbedding(query);
                if (queryEmbedding == null)
                {
                    Console.WriteLine("Failed to generate query embedding");
                    return semanticMatches;
                }

                // Process files in batches
                var batchSize = 5;
                var batches = filePaths.Select((path, index) => new { path, index })
                                      .GroupBy(x => x.index / batchSize)
                                      .Select(g => g.Select(x => x.path).ToList())
                                      .ToList();

                foreach (var batch in batches)
                {
                    var batchTasks = batch.Select(async filePath =>
                    {
                        return await ProcessFileForSemanticMatch(filePath, query, queryEmbedding, embeddingService);
                    });

                    var batchResults = await Task.WhenAll(batchTasks);
                    semanticMatches.AddRange(batchResults.Where(result => result.similarity > 0.1f)); // Lower threshold
                }

                return semanticMatches.OrderByDescending(m => m.similarity).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in semantic search: {ex.Message}");
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
        //private async Task<List<(string filePath, float similarity, List<string> relevantChunks, string documentType)>> PerformSemanticPreFiltering(string query, List<string> filePaths)
        //{
        //    var semanticMatches = new List<(string filePath, float similarity, List<string> relevantChunks, string documentType)>();

        //    try
        //    {
        //        Console.WriteLine($"Starting semantic pre-filtering for query: '{query}' across {filePaths.Count} files");

        //        // CRITICAL FIX: Detect exact search patterns (GUIDs, IDs, specific codes)
        //        if (IsExactSearchQuery(query))
        //        {
        //            Console.WriteLine("🎯 Detected exact search query - using keyword-based search for better precision");
        //            return await PerformKeywordPreFiltering(query, filePaths);
        //        }

        //        var modelPath = System.IO.Path.Combine("C:\\Users\\ukhan2\\Desktop\\ONNXModel", "model.onnx");
        //        if (!System.IO.File.Exists(modelPath))
        //        {
        //            Console.WriteLine("ONNX model not found, falling back to keyword-based filtering");
        //            return await PerformKeywordPreFiltering(query, filePaths);
        //        }

        //        using var embeddingService = new TextEmbeddingService(modelPath);
        //        var queryEmbedding = embeddingService?.GetEmbedding(query);
        //        if (queryEmbedding == null)
        //        {
        //            Console.WriteLine("Failed to generate query embedding, falling back to keyword filtering");
        //            return await PerformKeywordPreFiltering(query, filePaths);
        //        }

        //        // Debug: Check if query embedding is valid
        //        var queryEmbeddingSum = queryEmbedding.Sum();
        //        Console.WriteLine($"🔍 Query: '{query}' | Embedding sum: {queryEmbeddingSum:F3} | Length: {queryEmbedding.Length}");

        //        // Process files in batches for better performance
        //        var batchSize = 5;
        //        var batches = filePaths.Select((path, index) => new { path, index })
        //                              .GroupBy(x => x.index / batchSize)
        //                              .Select(g => g.Select(x => x.path).ToList())
        //                              .ToList();

        //        foreach (var batch in batches)
        //        {
        //            Console.WriteLine($"Processing batch of {batch.Count} files");

        //            var batchTasks = batch.Select(async filePath =>
        //            {
        //                return await ProcessFileForSemanticMatch(filePath, query, queryEmbedding, embeddingService);
        //            });

        //            var batchResults = await Task.WhenAll(batchTasks);
        //            semanticMatches.AddRange(batchResults.Where(result => result.similarity > 0));
        //        }

        //        // Sort by similarity and return top results
        //        Console.WriteLine($"Before filtering: {semanticMatches.Count} matches found");
        //        foreach (var match in semanticMatches.Take(5))
        //        {
        //            Console.WriteLine($"File: {System.IO.Path.GetFileName(match.filePath)}, Similarity: {match.similarity:F4}");
        //        }

        //        semanticMatches = semanticMatches.OrderByDescending(m => m.similarity)
        //                                        .Where(m => m.similarity > 0.03f) // Lowered threshold for exact name searches
        //                                        .ToList();

        //        Console.WriteLine($"Semantic pre-filtering completed: {semanticMatches.Count} relevant files found");
        //        return semanticMatches;
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine($"Error in semantic pre-filtering: {ex.Message}");
        //        return await PerformKeywordPreFiltering(query, filePaths);
        //    }
        //}

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
                var summary = content.Length > 100000 ? content.Substring(0, 100000) : content;
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

        private bool IsExactSearchQuery(string query)
        {
            if (string.IsNullOrEmpty(query)) return false;

            var queryLower = query.ToLower();
            
            // Detect GUID patterns (your specific case)
            var guidPattern = @"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b";
            if (System.Text.RegularExpressions.Regex.IsMatch(query, guidPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                Console.WriteLine("🔍 GUID pattern detected - using exact search");
                return true;
            }

            // Detect other exact search patterns
            var exactSearchIndicators = new[]
            {
                "guid", "id:", "uuid", "identifier",
                "exact:", "find:", "search for:",
                "document id", "file id", "unique id",
                "reference number", "tracking number"
            };

            foreach (var indicator in exactSearchIndicators)
            {
                if (queryLower.Contains(indicator))
                {
                    Console.WriteLine($"🔍 Exact search indicator '{indicator}' detected");
                    return true;
                }
            }

            // Detect patterns like alphanumeric codes, serial numbers
            var codePatterns = new[]
            {
                @"\b[A-Z0-9]{6,}\b", // Uppercase alphanumeric codes
                @"\b\d{6,}\b",       // Long numeric sequences
                @"\b[a-f0-9]{32}\b", // MD5-like hashes
                @"\b[A-Z0-9]+-\d+\b" // Codes with hyphens like "ABC-123"
            };

            foreach (var pattern in codePatterns)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(query, pattern))
                {
                    Console.WriteLine($"🔍 Code pattern detected: {pattern}");
                    return true;
                }
            }

            return false;
        }
    }
}