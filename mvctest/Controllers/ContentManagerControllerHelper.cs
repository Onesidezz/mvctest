using mvctest.Services;
using mvctest.Models;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace mvctest.Controllers
{
    public class ContentManagerHelperService
    {
        private readonly AppSettings _appSettings;
        private readonly ILuceneInterface _luceneInterface;

        public ContentManagerHelperService(IOptions<AppSettings> appSettings, ILuceneInterface luceneInterface)
        {
            _appSettings = appSettings.Value;
            _luceneInterface = luceneInterface;
        }
        public async Task<string> GetGenerativeAnswers(string query, string filePath, string content)
        {
            try
            {
                Console.WriteLine($"Generating AI answer for query: '{query}' from file: {System.IO.Path.GetFileName(filePath)}");

                var analysisPrompt = $@"
                                    Based on the content from the file '{System.IO.Path.GetFileName(filePath)}', please analyze and answer the following question.

                                    First, classify this document type from: Invoice, Contract, Report, Resume, TechnicalDoc, LegalBrief, FinancialStatement, SupportTicket, PolicyDocument, MarketingMaterial, MeetingMinutes, ResearchPaper, CreativeContent, Unknown

                                    Question: {query}

                                    File Content:
                                    {content}

                                    Instructions:
                                    - Start your response with the document classification in this exact format: [Document Type: YourClassification]
                                    - Then provide a direct, specific answer based only on the content above.
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

                    // Highlight search terms in the AI response
                    var queryWords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var highlightedAnswer = generativeAnswer.Trim();
                    foreach (var word in queryWords)
                    {
                        var regex = new System.Text.RegularExpressions.Regex($@"\b{System.Text.RegularExpressions.Regex.Escape(word)}\b",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        highlightedAnswer = regex.Replace(highlightedAnswer, $"<strong>$0</strong>");
                    }

                    // Return the AI response with highlighted search terms
                    return highlightedAnswer;
                }

                return "Unable to generate response from the document content.";
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
                using var httpClient = new HttpClient()
                {
                    // Increase timeout to 10 minutes for complex operations
                    Timeout = TimeSpan.FromMinutes(10)
                };

                var requestBody = new
                {
                    model = "qwen2.5:14b",//"gemma:7b",
                    prompt = prompt,
                    stream = false,
                    options = new
                    {
                        num_ctx = 32768, // Context window
                        num_predict = 4096, // Max response length
                        temperature = 0.3, // Consistent responses
                        num_thread = Environment.ProcessorCount

                    }
                };

                var jsonContent = JsonSerializer.Serialize(requestBody);
                var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                // Use cancellation token for additional control
                using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromMinutes(10));

                Console.WriteLine("Calling Ollama Gemma API with extended timeout...");
                var response = await httpClient.PostAsync("http://localhost:11434/api/generate", httpContent, cancellationTokenSource.Token);

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync(cancellationTokenSource.Token);
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
                    var errorContent = await response.Content.ReadAsStringAsync(cancellationTokenSource.Token);
                    Console.WriteLine($"✗ Ollama API error: {response.StatusCode} - {errorContent}");
                    return "";
                }
            }
            catch (OperationCanceledException ex)
            {
                Console.WriteLine($"✗ Ollama Gemma API timeout: {ex.Message}");
                return "Request timeout - the operation took too long. Please try with a shorter document or simpler query.";
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
        public async Task<string> GenerateAnswerWithEarlyTermination(string query, List<(string filePath, float relevanceScore, string relevantContent)> detailedResults)
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
            if (answer.Contains(":") || answer.Contains("•") || answer.Contains("-")) score += 0.2f; // Structured
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


        #region Enhanced Search Methods

        /// <summary>
        /// Main method to perform enhanced search with multiple levels and modes
        /// </summary>
        public async Task<EnhancedSearchResultsViewModel> PerformEnhancedSearch(AdvancedSearchParameters searchParams, SearchPerformanceMetrics performance)
        {
            var results = new EnhancedSearchResultsViewModel();
            var allResults = new List<SearchResultModel>();

            Console.WriteLine($"🚀 Starting enhanced search with mode: {searchParams.Mode}");

            switch (searchParams.Mode)
            {
                case SearchMode.WordLevel:
                    allResults = await PerformWordLevelSearch(searchParams, performance);
                    results.WordResults = allResults;
                    break;

                case SearchMode.SentenceLevel:
                    allResults = await PerformSentenceLevelSearch(searchParams, performance);
                    results.SentenceResults = allResults;
                    break;

                case SearchMode.DocumentLevel:
                    allResults = await PerformDocumentLevelSearch(searchParams, performance);
                    results.DocumentResults = allResults;
                    break;

                case SearchMode.Semantic:
                    allResults = await PerformSemanticSearch(searchParams, performance);
                    results.DocumentResults = allResults;
                    break;

                case SearchMode.Hybrid:
                    results = await PerformHybridMultiLevelSearch(searchParams, performance);
                    allResults = results.DocumentResults.Concat(results.WordResults).Concat(results.SentenceResults).ToList();
                    break;

                case SearchMode.Comprehensive:
                default:
                    results = await PerformComprehensiveSearch(searchParams, performance);
                    allResults = results.DocumentResults.Concat(results.WordResults).Concat(results.SentenceResults).ToList();
                    break;
            }

            // Apply sorting and filtering
            allResults = ApplyAdvancedSorting(allResults, searchParams.SortBy);
            allResults = ApplyAdvancedFiltering(allResults, searchParams);

            // Limit results
            if (searchParams.MaxResults.HasValue && allResults.Count > searchParams.MaxResults.Value)
            {
                allResults = allResults.Take(searchParams.MaxResults.Value).ToList();
            }

            // Update results in the view model
            if (searchParams.Mode == SearchMode.Comprehensive || searchParams.Mode == SearchMode.Hybrid)
            {
                // Results are already organized in results object
            }
            else
            {
                // Single mode - put results in appropriate category
                switch (searchParams.Mode)
                {
                    case SearchMode.WordLevel:
                        results.WordResults = allResults;
                        break;

                    case SearchMode.SentenceLevel:
                        results.SentenceResults = allResults;
                        break;

                    default:
                        results.DocumentResults = allResults;
                        break;
                }
            }

            results.TotalResults = results.DocumentResults.Count + results.WordResults.Count + results.SentenceResults.Count;

            Console.WriteLine($"✅ Enhanced search completed with {results.TotalResults} total results");
            return results;
        }

        /// <summary>
        /// Perform word-by-word level search using the new indexing structure
        /// </summary>
        private async Task<List<SearchResultModel>> PerformWordLevelSearch(AdvancedSearchParameters searchParams, SearchPerformanceMetrics performance)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine($"🔤 Starting word-level search for: '{searchParams.Query}'");

            try
            {
                // Build word-specific query
                var wordQuery = $"doc_type:word AND (word:\"{searchParams.Query}\" OR word_normalized:\"{searchParams.Query.ToLower()}\")";
                Console.WriteLine($"🔍 Word query: {wordQuery}");

                var wordResults = _luceneInterface.SearchFiles(wordQuery) ?? new List<SearchResultModel>();

                stopwatch.Stop();
                performance.WordSearchTime = stopwatch.Elapsed;
                performance.WordMatchesFound = wordResults.Count;

                Console.WriteLine($"✅ Word-level search completed: {wordResults.Count} matches in {stopwatch.ElapsedMilliseconds}ms");

                // Enhance results with word analysis data
                foreach (var result in wordResults)
                {
                    if (searchParams.ShowWordAnalysis)
                    {
                        var wordAnalysis = ExtractWordAnalysisFromResult(result);
                        if (wordAnalysis != null)
                        {
                            result.Metadata = result.Metadata ?? new Dictionary<string, string>();
                            result.Metadata["WordAnalysis"] = System.Text.Json.JsonSerializer.Serialize(wordAnalysis);
                        }
                    }
                }

                return wordResults;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                performance.WordSearchTime = stopwatch.Elapsed;
                Console.WriteLine($"❌ Error in word-level search: {ex.Message}");
                return new List<SearchResultModel>();
            }
        }

        /// <summary>
        /// Perform sentence-by-sentence level search
        /// </summary>
        private async Task<List<SearchResultModel>> PerformSentenceLevelSearch(AdvancedSearchParameters searchParams, SearchPerformanceMetrics performance)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine($"📝 Starting sentence-level search for: '{searchParams.Query}'");

            try
            {
                // Build sentence-specific query
                var sentenceQuery = $"doc_type:sentence AND sentence_content:\"{searchParams.Query}\"";
                Console.WriteLine($"🔍 Sentence query: {sentenceQuery}");

                var sentenceResults = _luceneInterface.SearchFiles(sentenceQuery) ?? new List<SearchResultModel>();

                stopwatch.Stop();
                performance.SentenceSearchTime = stopwatch.Elapsed;
                performance.SentenceMatchesFound = sentenceResults.Count;

                Console.WriteLine($"✅ Sentence-level search completed: {sentenceResults.Count} matches in {stopwatch.ElapsedMilliseconds}ms");

                // Enhance results with sentence context data
                foreach (var result in sentenceResults)
                {
                    if (searchParams.ShowSentenceContext)
                    {
                        var sentenceContext = ExtractSentenceContextFromResult(result);
                        if (sentenceContext != null)
                        {
                            result.Metadata = result.Metadata ?? new Dictionary<string, string>();
                            result.Metadata["SentenceContext"] = System.Text.Json.JsonSerializer.Serialize(sentenceContext);
                        }
                    }
                }

                return sentenceResults;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                performance.SentenceSearchTime = stopwatch.Elapsed;
                Console.WriteLine($"❌ Error in sentence-level search: {ex.Message}");
                return new List<SearchResultModel>();
            }
        }

        /// <summary>
        /// Perform document-level search (main documents only)
        /// </summary>
        private async Task<List<SearchResultModel>> PerformDocumentLevelSearch(AdvancedSearchParameters searchParams, SearchPerformanceMetrics performance)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine($"📄 Starting document-level search for: '{searchParams.Query}'");

            try
            {
                // Use existing comprehensive search but filter for main documents
                var documentResults = _luceneInterface.SearchFiles(searchParams.Query) ?? new List<SearchResultModel>();

                // Filter out word and sentence results, keep only main documents
                documentResults = documentResults.Where(r =>
                    r.Metadata?.ContainsKey("doc_type") != true ||
                    r.Metadata["doc_type"] == "document" ||
                    string.IsNullOrEmpty(r.Metadata.GetValueOrDefault("doc_type"))).ToList();

                stopwatch.Stop();
                performance.DocumentSearchTime = stopwatch.Elapsed;
                performance.DocumentMatchesFound = documentResults.Count;

                Console.WriteLine($"✅ Document-level search completed: {documentResults.Count} matches in {stopwatch.ElapsedMilliseconds}ms");

                return documentResults;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                performance.DocumentSearchTime = stopwatch.Elapsed;
                Console.WriteLine($"❌ Error in document-level search: {ex.Message}");
                return new List<SearchResultModel>();
            }
        }

        /// <summary>
        /// Perform semantic search using embeddings
        /// </summary>
        private async Task<List<SearchResultModel>> PerformSemanticSearch(AdvancedSearchParameters searchParams, SearchPerformanceMetrics performance)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine($"🧠 Starting semantic search for: '{searchParams.Query}'");

            try
            {
                var semanticResults = _luceneInterface.SemanticSearch(searchParams.Query, maxResults: searchParams.MaxResults ?? 50) ?? new List<SearchResultModel>();

                stopwatch.Stop();
                performance.SemanticSearchTime = stopwatch.Elapsed;
                performance.DocumentMatchesFound = semanticResults.Count;

                Console.WriteLine($"✅ Semantic search completed: {semanticResults.Count} matches in {stopwatch.ElapsedMilliseconds}ms");

                return semanticResults;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                performance.SemanticSearchTime = stopwatch.Elapsed;
                Console.WriteLine($"❌ Error in semantic search: {ex.Message}");
                return new List<SearchResultModel>();
            }
        }

        /// <summary>
        /// Perform hybrid search combining multiple approaches
        /// </summary>
        private async Task<EnhancedSearchResultsViewModel> PerformHybridMultiLevelSearch(AdvancedSearchParameters searchParams, SearchPerformanceMetrics performance)
        {
            Console.WriteLine($"🔀 Starting hybrid multi-level search for: '{searchParams.Query}'");

            var results = new EnhancedSearchResultsViewModel();

            // Run all search types in parallel for better performance
            var tasks = new List<Task<List<SearchResultModel>>>();
            var searchTypes = new List<string>();

            // Always include document search
            tasks.Add(PerformDocumentLevelSearch(searchParams, performance));
            searchTypes.Add("Document");

            // Add word search if query is suitable for word-level search
            if (IsWordLevelSearchBeneficial(searchParams.Query))
            {
                tasks.Add(PerformWordLevelSearch(searchParams, performance));
                searchTypes.Add("Word");
            }

            // Add sentence search if query is suitable
            if (IsSentenceLevelSearchBeneficial(searchParams.Query))
            {
                tasks.Add(PerformSentenceLevelSearch(searchParams, performance));
                searchTypes.Add("Sentence");
            }

            // Execute all searches
            var searchResults = await Task.WhenAll(tasks);

            // Organize results
            for (int i = 0; i < searchResults.Length; i++)
            {
                var resultList = searchResults[i];
                var searchType = searchTypes[i];

                switch (searchType)
                {
                    case "Document":
                        results.DocumentResults = resultList;
                        break;

                    case "Word":
                        results.WordResults = resultList;
                        break;

                    case "Sentence":
                        results.SentenceResults = resultList;
                        break;
                }
            }

            Console.WriteLine($"✅ Hybrid search completed: Docs={results.DocumentResults.Count}, Words={results.WordResults.Count}, Sentences={results.SentenceResults.Count}");

            return results;
        }

        /// <summary>
        /// Perform comprehensive search across all levels
        /// </summary>
        private async Task<EnhancedSearchResultsViewModel> PerformComprehensiveSearch(AdvancedSearchParameters searchParams, SearchPerformanceMetrics performance)
        {
            Console.WriteLine($"🎯 Starting comprehensive search for: '{searchParams.Query}'");

            var results = new EnhancedSearchResultsViewModel();

            // Run all search types in parallel
            var documentTask = PerformDocumentLevelSearch(searchParams, performance);
            var wordTask = PerformWordLevelSearch(searchParams, performance);
            var sentenceTask = PerformSentenceLevelSearch(searchParams, performance);

            await Task.WhenAll(documentTask, wordTask, sentenceTask);

            results.DocumentResults = await documentTask;
            results.WordResults = await wordTask;
            results.SentenceResults = await sentenceTask;

            Console.WriteLine($"✅ Comprehensive search completed: Docs={results.DocumentResults.Count}, Words={results.WordResults.Count}, Sentences={results.SentenceResults.Count}");

            return results;
        }

        #endregion Enhanced Search Methods

        #region Helper Methods for Enhanced Search

        /// <summary>
        /// Extract word analysis data from search result
        /// </summary>
        private WordAnalysisData? ExtractWordAnalysisFromResult(SearchResultModel result)
        {
            try
            {
                if (result.Content == null) return null;

                // Parse the word result content to extract analysis data
                var lines = result.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var wordAnalysis = new WordAnalysisData();

                foreach (var line in lines)
                {
                    if (line.StartsWith("Word:"))
                        wordAnalysis.Word = line.Substring(5).Trim();
                    else if (line.StartsWith("Frequency:"))
                    {
                        var freqValue = 0;
                        if (int.TryParse(line.Substring(10).Trim(), out freqValue))
                            wordAnalysis.Frequency = freqValue;
                    }
                    else if (line.StartsWith("Positions:"))
                    {
                        var positionsStr = line.Substring(10).Trim();
                        wordAnalysis.Positions = positionsStr.Split(',')
                            .Select(p => int.TryParse(p.Trim(), out var pos) ? pos : 0)
                            .Where(p => p > 0).ToList();
                        if (wordAnalysis.Positions.Any())
                            wordAnalysis.FirstPosition = wordAnalysis.Positions.First();
                    }
                    else if (line.StartsWith("Context:"))
                        wordAnalysis.Context = line.Substring(8).Trim();
                }

                return wordAnalysis;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting word analysis: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Extract sentence context data from search result
        /// </summary>
        private SentenceContextData? ExtractSentenceContextFromResult(SearchResultModel result)
        {
            try
            {
                if (result.Content == null) return null;

                var lines = result.Content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var sentenceContext = new SentenceContextData();

                foreach (var line in lines)
                {
                    if (line.StartsWith("Sentence:"))
                        sentenceContext.Sentence = line.Substring(9).Trim();
                    else if (line.StartsWith("Index:"))
                    {
                        var indexValue = 0;
                        if (int.TryParse(line.Substring(6).Trim(), out indexValue))
                            sentenceContext.SentenceIndex = indexValue;
                    }
                    else if (line.StartsWith("Previous:"))
                        sentenceContext.PreviousSentence = line.Substring(9).Trim();
                    else if (line.StartsWith("Next:"))
                        sentenceContext.NextSentence = line.Substring(5).Trim();
                    else if (line.StartsWith("File:"))
                        sentenceContext.ParentFile = line.Substring(5).Trim();
                }

                sentenceContext.ParentFile = result.FilePath ?? result.FileName ?? "";

                return sentenceContext;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting sentence context: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Determine if word-level search would be beneficial for the query
        /// </summary>
        private bool IsWordLevelSearchBeneficial(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return false;

            var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // Benefit from word search if:
            // 1. Query is a single word
            // 2. Query contains specific terms we want to analyze frequency for
            // 3. Query is asking about word frequency or usage
            return words.Length == 1 ||
                   query.ToLower().Contains("frequency") ||
                   query.ToLower().Contains("how often") ||
                   query.ToLower().Contains("occurrences");
        }

        /// <summary>
        /// Determine if sentence-level search would be beneficial for the query
        /// </summary>
        private bool IsSentenceLevelSearchBeneficial(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return false;

            var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // Benefit from sentence search if:
            // 1. Query is a phrase (multiple words)
            // 2. Query contains quotation marks
            // 3. Query is asking for context or explanations
            return words.Length > 1 ||
                   query.Contains('"') ||
                   query.ToLower().Contains("context") ||
                   query.ToLower().Contains("explain") ||
                   query.ToLower().Contains("what") ||
                   query.ToLower().Contains("how") ||
                   query.ToLower().Contains("why");
        }

        /// <summary>
        /// Apply advanced sorting to search results
        /// </summary>
        private List<SearchResultModel> ApplyAdvancedSorting(List<SearchResultModel> results, SearchResultSort sortBy)
        {
            return sortBy switch
            {
                SearchResultSort.Relevance => results.OrderByDescending(r => r.Score).ToList(),
                SearchResultSort.Date => results.OrderByDescending(r => DateTime.TryParse(r.date, out var date) ? date : DateTime.MinValue).ToList(),
                SearchResultSort.FileName => results.OrderBy(r => r.FileName).ToList(),
                SearchResultSort.FileSize => results.OrderByDescending(r => GetFileSizeFromResult(r)).ToList(),
                SearchResultSort.WordFrequency => results.OrderByDescending(r => GetWordFrequencyFromResult(r)).ToList(),
                SearchResultSort.SentenceIndex => results.OrderBy(r => GetSentenceIndexFromResult(r)).ToList(),
                _ => results.OrderByDescending(r => r.Score).ToList()
            };
        }

        /// <summary>
        /// Apply advanced filtering to search results
        /// </summary>
        private List<SearchResultModel> ApplyAdvancedFiltering(List<SearchResultModel> results, AdvancedSearchParameters searchParams)
        {
            var filteredResults = results.AsEnumerable();

            // Filter by file type
            if (!string.IsNullOrEmpty(searchParams.FileType))
            {
                filteredResults = filteredResults.Where(r =>
                    Path.GetExtension(r.FilePath ?? "").Equals($".{searchParams.FileType}", StringComparison.OrdinalIgnoreCase));
            }

            // Filter by date range
            if (searchParams.DateFrom.HasValue)
            {
                filteredResults = filteredResults.Where(r =>
                    DateTime.TryParse(r.date, out var date) && date >= searchParams.DateFrom.Value);
            }

            if (searchParams.DateTo.HasValue)
            {
                filteredResults = filteredResults.Where(r =>
                    DateTime.TryParse(r.date, out var date) && date <= searchParams.DateTo.Value);
            }

            // Filter by minimum word count (if applicable)
            if (searchParams.MinWordCount.HasValue)
            {
                filteredResults = filteredResults.Where(r => GetWordCountFromResult(r) >= searchParams.MinWordCount.Value);
            }

            return filteredResults.ToList();
        }

        private long GetFileSizeFromResult(SearchResultModel result)
        {
            try
            {
                if (System.IO.File.Exists(result.FilePath))
                    return new FileInfo(result.FilePath).Length;
            }
            catch { }
            return 0;
        }

        private int GetWordFrequencyFromResult(SearchResultModel result)
        {
            if (result.Metadata?.TryGetValue("frequency", out var freqStr) == true)
                return int.TryParse(freqStr, out var freq) ? freq : 0;
            return 0;
        }

        private int GetSentenceIndexFromResult(SearchResultModel result)
        {
            if (result.Metadata?.TryGetValue("sentence_index", out var indexStr) == true)
                return int.TryParse(indexStr, out var index) ? index : 0;
            return 0;
        }

        private int GetWordCountFromResult(SearchResultModel result)
        {
            if (result.Content != null)
                return result.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            return 0;
        }

        /// <summary>
        /// Detect optimal search mode based on query characteristics
        /// </summary>
        public SearchMode DetectOptimalSearchMode(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return SearchMode.Comprehensive;

            var queryLower = query.ToLower();
            var words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            // Word-level search indicators
            if (words.Length == 1 ||
                queryLower.Contains("frequency") ||
                queryLower.Contains("how often") ||
                queryLower.Contains("count of") ||
                queryLower.Contains("occurrences"))
            {
                Console.WriteLine("🔤 Query suggests word-level analysis");
                return SearchMode.WordLevel;
            }

            // Sentence-level search indicators
            if (query.Contains('"') ||
                queryLower.Contains("sentence") ||
                queryLower.Contains("context") ||
                queryLower.Contains("explain") ||
                queryLower.Contains("what does") ||
                queryLower.Contains("meaning of") ||
                (words.Length >= 3 && words.Length <= 8))
            {
                Console.WriteLine("📝 Query suggests sentence-level analysis");
                return SearchMode.SentenceLevel;
            }

            // Semantic search indicators
            if (queryLower.Contains("similar to") ||
                queryLower.Contains("related to") ||
                queryLower.Contains("like") ||
                queryLower.Contains("about") ||
                words.Length > 8)
            {
                Console.WriteLine("🧠 Query suggests semantic search");
                return SearchMode.Semantic;
            }

            // Default to hybrid for complex queries
            if (words.Length > 2)
            {
                Console.WriteLine("🔀 Query suggests hybrid multi-level search");
                return SearchMode.Hybrid;
            }

            Console.WriteLine("🎯 Using comprehensive search as default");
            return SearchMode.Comprehensive;
        }

        /// <summary>
        /// Perform enhanced search with path filtering
        /// </summary>
        public async Task<EnhancedSearchResultsViewModel> PerformEnhancedPathSearch(
            AdvancedSearchParameters searchParams,
            List<string> filePaths,
            SearchPerformanceMetrics performance,
            bool fromAnalytics,
            ILuceneInterface luceneInterface)
        {
            Console.WriteLine($"🚀 Starting enhanced path search for {filePaths.Count} specific files");

            var results = new EnhancedSearchResultsViewModel();

            if (!fromAnalytics)
            {
                switch (searchParams.Mode)
                {
                    case SearchMode.WordLevel:
                        results.WordResults = await PerformWordLevelPathSearch(searchParams.Query, filePaths, performance, _luceneInterface);
                        break;

                    case SearchMode.SentenceLevel:
                        results.SentenceResults = await PerformSentenceLevelPathSearch(searchParams.Query, filePaths, performance, _luceneInterface);
                        break;

                    case SearchMode.DocumentLevel:
                        results.DocumentResults = await PerformDocumentLevelPathSearch(searchParams.Query, filePaths, performance, _luceneInterface);
                        break;

                    case SearchMode.Hybrid:
                    case SearchMode.Comprehensive:
                    default:
                        // Run all search types in parallel
                        var documentTask1 = PerformDocumentLevelPathSearch(searchParams.Query, filePaths, performance, _luceneInterface);
                        var wordTask1 = PerformWordLevelPathSearch(searchParams.Query, filePaths, performance, _luceneInterface);
                        var sentenceTask1 = PerformSentenceLevelPathSearch(searchParams.Query, filePaths, performance, _luceneInterface);

                        await Task.WhenAll(documentTask1, wordTask1, sentenceTask1);

                        results.DocumentResults = await documentTask1;
                        results.WordResults = await wordTask1;
                        results.SentenceResults = await sentenceTask1;
                        break;
                }
            }
            else
            {
                // Analytics mode always runs all searches
                var documentTask2 = PerformDocumentLevelPathSearch(searchParams.Query, filePaths, performance, _luceneInterface);
                var wordTask2 = PerformWordLevelPathSearch(searchParams.Query, filePaths, performance, _luceneInterface);
                var sentenceTask2 = PerformSentenceLevelPathSearch(searchParams.Query, filePaths, performance, _luceneInterface);

                await Task.WhenAll(documentTask2, wordTask2, sentenceTask2);

                results.DocumentResults = await documentTask2;
                results.WordResults = await wordTask2;
                results.SentenceResults = await sentenceTask2;
            }

            // Calculate total results count
            results.TotalResults =
                (results.DocumentResults?.Count ?? 0) +
                (results.WordResults?.Count ?? 0) +
                (results.SentenceResults?.Count ?? 0);

            Console.WriteLine($"✅ Enhanced path search completed: {results.TotalResults} total results");
            return results;
        }

        /// <summary>
        /// Perform word-level search on specific file paths
        /// </summary>
        private async Task<List<SearchResultModel>> PerformWordLevelPathSearch(string query, List<string> filePaths, SearchPerformanceMetrics performance, ILuceneInterface luceneInterface)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine($"🔤 Word-level search in {filePaths.Count} specific files");

            try
            {
                var results = luceneInterface.SearchFilesInPaths(query, filePaths) ?? new List<SearchResultModel>();

                // Filter for word-level results
                var wordResults = results.Where(r =>
                    r.Metadata?.GetValueOrDefault("doc_type") == "word" ||
                    r.Snippets?.Any(s => s.Contains("Word:") || s.Contains("Frequency:")) == true
                ).ToList();

                stopwatch.Stop();
                performance.WordSearchTime = stopwatch.Elapsed;
                performance.WordMatchesFound = wordResults.Count;

                Console.WriteLine($"✅ Word-level path search: {wordResults.Count} matches in {stopwatch.ElapsedMilliseconds}ms");
                return wordResults;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Console.WriteLine($"❌ Error in word-level path search: {ex.Message}");
                return new List<SearchResultModel>();
            }
        }

        /// <summary>
        /// Perform sentence-level search on specific file paths
        /// </summary>
        private async Task<List<SearchResultModel>> PerformSentenceLevelPathSearch(string query, List<string> filePaths, SearchPerformanceMetrics performance, ILuceneInterface luceneInterface)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine($"📝 Sentence-level search in {filePaths.Count} specific files");

            try
            {
                var results = luceneInterface.SearchFilesInPaths(query, filePaths) ?? new List<SearchResultModel>();

                // Filter for sentence-level results
                var sentenceResults = results.Where(r =>
                    r.Metadata?.GetValueOrDefault("doc_type") == "sentence" ||
                    r.Snippets?.Any(s => s.Contains("Sentence") && s.Contains(":")) == true
                ).ToList();

                stopwatch.Stop();
                performance.SentenceSearchTime = stopwatch.Elapsed;
                performance.SentenceMatchesFound = sentenceResults.Count;

                Console.WriteLine($"✅ Sentence-level path search: {sentenceResults.Count} matches in {stopwatch.ElapsedMilliseconds}ms");
                return sentenceResults;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Console.WriteLine($"❌ Error in sentence-level path search: {ex.Message}");
                return new List<SearchResultModel>();
            }
        }

        /// <summary>
        /// Perform document-level search on specific file paths
        /// </summary>
        private async Task<List<SearchResultModel>> PerformDocumentLevelPathSearch(string query, List<string> filePaths, SearchPerformanceMetrics performance, ILuceneInterface luceneInterface)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine($"📄 Document-level search in {filePaths.Count} specific files");

            try
            {
                var results = luceneInterface.SearchFilesInPaths(query, filePaths) ?? new List<SearchResultModel>();

                // Filter for main document results (exclude word/sentence level)
                var documentResults = results.Where(r =>
                    r.Metadata?.ContainsKey("doc_type") != true ||
                    r.Metadata["doc_type"] == "document" ||
                    string.IsNullOrEmpty(r.Metadata.GetValueOrDefault("doc_type"))
                ).ToList();

                stopwatch.Stop();
                performance.DocumentSearchTime = stopwatch.Elapsed;
                performance.DocumentMatchesFound = documentResults.Count;

                Console.WriteLine($"✅ Document-level path search: {documentResults.Count} matches in {stopwatch.ElapsedMilliseconds}ms");
                return documentResults;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Console.WriteLine($"❌ Error in document-level path search: {ex.Message}");
                return new List<SearchResultModel>();
            }
        }

        #endregion Helper Methods for Enhanced Search

        public string ExtractCompleteSentence(string snippet, string searchQuery = "")
        {
            if (string.IsNullOrWhiteSpace(snippet))
                return string.Empty;

            try
            {
                // Remove "Sentence N:" prefix if it exists
                var cleanSnippet = System.Text.RegularExpressions.Regex.Replace(snippet, @"^Sentence \d+:\s*", "").Trim();
                
                if (string.IsNullOrWhiteSpace(cleanSnippet))
                    return string.Empty;

                // Debug logging for Excel files
                if (cleanSnippet.Contains(" | ") || cleanSnippet.Contains("[ROW") || cleanSnippet.Contains("[SHEET"))
                {
                    Console.WriteLine($"Processing Excel snippet: {cleanSnippet.Substring(0, Math.Min(100, cleanSnippet.Length))}...");
                }

                // Handle Excel structured data (detect by presence of [ROW], [SHEET] markers, or Excel-specific content)
                if (cleanSnippet.Contains("[ROW") || cleanSnippet.Contains("[SHEET") || 
                    cleanSnippet.Contains("Document GUID:") || cleanSnippet.Contains("Generated:") ||
                    cleanSnippet.Contains("[END ROW"))
                {
                    return ExtractStructuredDataSnippet(cleanSnippet, searchQuery);
                }

                // If the snippet already looks like a complete sentence, return it
                if (cleanSnippet.Length > 10 && cleanSnippet.EndsWith('.'))
                {
                    return cleanSnippet;
                }

                // Remove any HTML tags like <strong> for text processing, but keep original for return
                var textOnly = System.Text.RegularExpressions.Regex.Replace(cleanSnippet, @"<[^>]+>", "");
                
                if (string.IsNullOrWhiteSpace(textOnly))
                    return cleanSnippet; // Return original if only HTML tags

                // Find sentence boundaries using regex (more reliable)
                var sentences = System.Text.RegularExpressions.Regex.Split(textOnly, @"(?<=[.!?])\s+(?=[A-Z])")
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();

                if (sentences.Count == 0)
                    return cleanSnippet;

                // If we have multiple sentences, take the first complete one
                var firstSentence = sentences[0].Trim();
                
                // If the first sentence doesn't end with punctuation, try to find a complete sentence
                if (!firstSentence.EndsWith('.') && !firstSentence.EndsWith('!') && !firstSentence.EndsWith('?'))
                {
                    // Look for the first complete sentence
                    var completeSentence = sentences.FirstOrDefault(s => s.Trim().EndsWith('.') || s.Trim().EndsWith('!') || s.Trim().EndsWith('?'));
                    if (!string.IsNullOrEmpty(completeSentence))
                    {
                        firstSentence = completeSentence.Trim();
                    }
                    else
                    {
                        // If no complete sentence found, add a period to the first sentence
                        firstSentence = firstSentence.TrimEnd() + ".";
                    }
                }

                // Map back to original with HTML tags preserved
                var result = MapSentenceBackToOriginal(cleanSnippet, firstSentence);
                return string.IsNullOrWhiteSpace(result) ? cleanSnippet : result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ExtractCompleteSentence: {ex.Message}");
                return snippet; // Return original on any error
            }
        }

        private string MapSentenceBackToOriginal(string originalWithTags, string textOnlySentence)
        {
            if (string.IsNullOrWhiteSpace(textOnlySentence))
                return string.Empty;

            try
            {
                // Simple approach: find the text in the original and extract up to its end
                var textOnly = System.Text.RegularExpressions.Regex.Replace(originalWithTags, @"<[^>]+>", "");
                var endIndex = textOnly.IndexOf(textOnlySentence.TrimEnd('.', '!', '?'));
                
                if (endIndex >= 0)
                {
                    var endPos = endIndex + textOnlySentence.TrimEnd('.', '!', '?').Length;
                    
                    // Find corresponding position in original with tags
                    var result = new System.Text.StringBuilder();
                    var originalIndex = 0;
                    var textOnlyIndex = 0;
                    
                    while (originalIndex < originalWithTags.Length && textOnlyIndex <= endPos)
                    {
                        if (originalWithTags[originalIndex] == '<')
                        {
                            // Copy HTML tag
                            while (originalIndex < originalWithTags.Length && originalWithTags[originalIndex] != '>')
                            {
                                result.Append(originalWithTags[originalIndex]);
                                originalIndex++;
                            }
                            if (originalIndex < originalWithTags.Length)
                            {
                                result.Append(originalWithTags[originalIndex]);
                                originalIndex++;
                            }
                        }
                        else
                        {
                            result.Append(originalWithTags[originalIndex]);
                            originalIndex++;
                            textOnlyIndex++;
                        }
                    }
                    
                    var finalResult = result.ToString().Trim();
                    if (!finalResult.EndsWith('.') && !finalResult.EndsWith('!') && !finalResult.EndsWith('?'))
                    {
                        finalResult += ".";
                    }
                    
                    return finalResult;
                }
            }
            catch
            {
                // If mapping fails, return original
            }

            return originalWithTags;
        }

        private string ExtractStructuredDataSnippet(string snippet, string searchQuery = "")
        {
            try
            {
                Console.WriteLine($"ExtractStructuredDataSnippet called with: {snippet.Substring(0, Math.Min(200, snippet.Length))}...");
                
                // For Excel structured data, extract meaningful content while preserving structure
                var lines = snippet.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => line.Trim())
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .ToList();

                var dataRows = new List<string>();
                bool foundHeader = false;
                string headerRow = "";

                foreach (var line in lines)
                {
                    // Skip structural markers that don't contain data
                    if (line.StartsWith("[ROW") && line.EndsWith("]"))
                        continue;
                    if (line.StartsWith("[END ROW") && line.EndsWith("]"))
                        continue;
                    if (line.StartsWith("[SHEET") && line.EndsWith("]"))
                        continue;
                    if (line.StartsWith("[END SHEET") && line.EndsWith("]"))
                        continue;
                    if (line.StartsWith("Document GUID:") || line.StartsWith("Generated:"))
                        continue; // Skip metadata lines

                    // Check if this looks like a header row (contains common column names)
                    if (!foundHeader && IsHeaderRow(line))
                    {
                        headerRow = line;
                        foundHeader = true;
                        Console.WriteLine($"Found header row: {line}");
                        continue;
                    }

                    // Keep lines with actual data (not starting with [ and having multiple words/numbers)
                    if (!line.StartsWith("[") && !line.EndsWith("]") && line.Split(' ').Length >= 3)
                    {
                        dataRows.Add(line);
                        Console.WriteLine($"Added data row: {line}");
                    }
                }

                Console.WriteLine($"Found {dataRows.Count} data rows");

                // If we have meaningful content, format it nicely
                if (dataRows.Any())
                {
                    var result = new StringBuilder();
                    
                    // Add header if we found one
                    if (!string.IsNullOrEmpty(headerRow))
                    {
                        // Highlight search terms first, then format the header
                        var highlightedHeader = HighlightSearchTerms(headerRow, searchQuery);
                        result.Append("Headers: " + FormatExcelRow(highlightedHeader));
                        result.Append(" | ");
                    }

                    // Filter rows that contain search terms if search query is provided
                    var relevantRows = dataRows;
                    if (!string.IsNullOrEmpty(searchQuery))
                    {
                        var searchTerms = searchQuery.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                        relevantRows = dataRows.Where(row => 
                            searchTerms.Any(term => row.Contains(term, StringComparison.OrdinalIgnoreCase))
                        ).ToList();
                        
                        // If no rows contain search terms, fall back to all rows
                        if (!relevantRows.Any())
                        {
                            relevantRows = dataRows;
                        }
                    }

                    // Limit to first 2 relevant rows to avoid overly long snippets
                    var limitedRows = relevantRows.Take(2).ToList();
                    
                    var formattedRows = new List<string>();
                    foreach (var row in limitedRows)
                    {
                        // Highlight search terms first, then format the row
                        var highlightedRow = HighlightSearchTerms(row, searchQuery);
                        formattedRows.Add(FormatExcelRow(highlightedRow));
                    }
                    
                    result.Append("Data: " + string.Join(" • ", formattedRows));
                    
                    // Add note if search terms were found
                    if (!string.IsNullOrEmpty(searchQuery) && relevantRows.Count < dataRows.Count)
                    {
                        result.Append($" (showing {limitedRows.Count} of {relevantRows.Count} matching rows)");
                    }
                    
                    // Ensure it ends with proper punctuation
                    var resultString = result.ToString();
                    if (!resultString.EndsWith('.') && !resultString.EndsWith('!') && !resultString.EndsWith('?') && !resultString.EndsWith(')'))
                    {
                        resultString += ".";
                    }
                    
                    Console.WriteLine($"Returning formatted result with search terms: {resultString}");
                    return resultString;
                }

                Console.WriteLine("No meaningful lines found, returning fallback");
                // Fallback: return original snippet with added punctuation
                var fallback = snippet.Trim();
                if (fallback.Length > 200)
                {
                    fallback = fallback.Substring(0, 200) + "...";
                }
                if (!fallback.EndsWith('.') && !fallback.EndsWith('!') && !fallback.EndsWith('?'))
                {
                    fallback += ".";
                }
                return fallback;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ExtractStructuredDataSnippet: {ex.Message}");
                // Return original with punctuation as fallback
                var fallback = snippet.Trim();
                if (fallback.Length > 200)
                {
                    fallback = fallback.Substring(0, 200) + "...";
                }
                if (!fallback.EndsWith('.') && !fallback.EndsWith('!') && !fallback.EndsWith('?'))
                {
                    fallback += ".";
                }
                return fallback;
            }
        }

        private bool IsHeaderRow(string line)
        {
            var commonHeaders = new[] { "ID", "Date", "Category", "Description", "Value", "Status", "Name", "Type", "Amount", "Code", "Title", "Email", "Phone", "Address" };
            var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            
            // If line has multiple words that match common headers, it's likely a header
            var headerMatches = words.Count(word => commonHeaders.Any(header => 
                string.Equals(word, header, StringComparison.OrdinalIgnoreCase)));
                
            return headerMatches >= 2; // Need at least 2 header-like words
        }

        private string FormatExcelRow(string row)
        {
            try
            {
                // If row contains highlighting tags, preserve them during formatting
                if (row.Contains("<strong>"))
                {
                    // For highlighted content, be more generous with length to preserve search terms
                    return row.Length > 200 ? row.Substring(0, 200) + "..." : row;
                }

                var parts = row.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                
                // For very long rows, show first few and last few parts
                if (parts.Length > 8)
                {
                    var firstParts = parts.Take(4);
                    var lastParts = parts.TakeLast(2);
                    return string.Join(" ", firstParts) + " ... " + string.Join(" ", lastParts);
                }
                else if (parts.Length > 6)
                {
                    // Show first 6 parts
                    return string.Join(" ", parts.Take(6)) + "...";
                }
                else
                {
                    return row;
                }
            }
            catch
            {
                return row.Length > 50 ? row.Substring(0, 50) + "..." : row;
            }
        }

        private string HighlightSearchTerms(string text, string searchQuery)
        {
            if (string.IsNullOrEmpty(searchQuery) || string.IsNullOrEmpty(text))
                return text;

            try
            {
                var searchTerms = searchQuery.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                var result = text;

                foreach (var term in searchTerms)
                {
                    if (term.Length > 1) // Skip single character terms
                    {
                        // Use case-insensitive replacement with <strong> tags for highlighting
                        var pattern = System.Text.RegularExpressions.Regex.Escape(term);
                        result = System.Text.RegularExpressions.Regex.Replace(
                            result, 
                            pattern, 
                            match => $"<strong>{match.Value}</strong>", 
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase
                        );
                    }
                }

                Console.WriteLine($"Highlighted '{searchQuery}' in text: {result.Substring(0, Math.Min(150, result.Length))}...");
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error highlighting search terms: {ex.Message}");
                return text; // Return original text if highlighting fails
            }
        }
    }
}