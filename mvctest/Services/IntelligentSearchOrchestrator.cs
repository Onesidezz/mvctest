using System.Diagnostics;
using System.Text.RegularExpressions;
using mvctest.Models;

namespace mvctest.Services
{
    public class IntelligentSearchOrchestrator
    {
        private readonly ILuceneInterface _luceneInterface;
        private readonly ILogger<IntelligentSearchOrchestrator> _logger;
        
        public IntelligentSearchOrchestrator(
            ILuceneInterface luceneInterface,
            ILogger<IntelligentSearchOrchestrator> logger)
        {
            _luceneInterface = luceneInterface;
            _logger = logger;
        }

        public async Task<List<(string filePath, float similarity, List<string> relevantChunks, string documentType)>> SmartSearchAsync(string query, List<string> filePaths)
        {
            var stopwatch = Stopwatch.StartNew();
            
            // Step 1: Analyze query intent
            var queryAnalysis = AnalyzeQueryIntent(query);
            _logger.LogInformation($"🎯 Query Analysis: Type={queryAnalysis.Type}, HasIdentifier={queryAnalysis.HasExactIdentifier}");
            
            // Step 2: Choose optimal search strategy
            List<(string filePath, float similarity, List<string> relevantChunks, string documentType)> results;
            
            if (queryAnalysis.HasExactIdentifier)
            {
                // For queries with GUIDs/IDs, use exact match first
                _logger.LogInformation("🔍 Using EXACT IDENTIFIER search strategy");
                results = await PerformExactIdentifierSearch(query, queryAnalysis.Identifiers, filePaths);
            }
            else if (queryAnalysis.Type == QueryIntentType.NavigationalSearch)
            {
                // Looking for specific named entities
                _logger.LogInformation("🎯 Using NAVIGATIONAL search strategy (hybrid with exact priority)");
                results = await PerformHybridSearch(query, filePaths, prioritizeExact: true);
            }
            else
            {
                // Natural language queries
                _logger.LogInformation("🧠 Using NATURAL LANGUAGE search strategy (semantic priority)");
                results = await PerformHybridSearch(query, filePaths, prioritizeExact: false);
            }
            
            _logger.LogInformation($"Smart search completed in {stopwatch.ElapsedMilliseconds}ms, found {results.Count} results");
            return results.OrderByDescending(r => r.similarity).ToList();
        }
        
        private QueryAnalysis AnalyzeQueryIntent(string query)
        {
            var analysis = new QueryAnalysis
            {
                OriginalQuery = query,
                Identifiers = new List<string>(),
                NamedEntities = new List<string>()
            };
            
            // Extract GUIDs and identifiers (more flexible pattern)
            var guidPattern = @"[a-fA-F0-9]{8,}(?:-[a-fA-F0-9]{4,})*";
            var guidMatches = Regex.Matches(query, guidPattern);
            foreach (Match match in guidMatches)
            {
                if (match.Value.Length >= 8) // Significant identifier
                {
                    analysis.Identifiers.Add(match.Value);
                    analysis.HasExactIdentifier = true;
                    _logger.LogInformation($"🔍 Found identifier: {match.Value}");
                }
            }
            
            // Extract proper names (Capitalized words)
            var namePattern = @"\b[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*\b";
            var nameMatches = Regex.Matches(query, namePattern);
            foreach (Match match in nameMatches)
            {
                analysis.NamedEntities.Add(match.Value);
                _logger.LogInformation($"👤 Found named entity: {match.Value}");
            }
            
            // Determine query type
            if (analysis.HasExactIdentifier)
            {
                analysis.Type = QueryIntentType.ExactIdentifierSearch;
            }
            else if (analysis.NamedEntities.Count > 0 && query.Split(' ').Length <= 6)
            {
                analysis.Type = QueryIntentType.NavigationalSearch;
            }
            else
            {
                analysis.Type = QueryIntentType.InformationalSearch;
            }
            
            return analysis;
        }
        
        private async Task<List<(string filePath, float similarity, List<string> relevantChunks, string documentType)>> PerformExactIdentifierSearch(
            string query, 
            List<string> identifiers, 
            List<string> filePaths)
        {
            _logger.LogInformation($"🔍 Performing EXACT identifier search for: {string.Join(", ", identifiers)}");
            
            var results = new List<(string filePath, float similarity, List<string> relevantChunks, string documentType)>();
            var processedFiles = 0;
            
            await Task.Run(() =>
            {
                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                };
                
                var lockObj = new object();
                
                Parallel.ForEach(filePaths, parallelOptions, filePath =>
                {
                    try
                    {
                        var content = ExtractFileContent(filePath);
                        if (string.IsNullOrEmpty(content)) return;
                        
                        Interlocked.Increment(ref processedFiles);
                        
                        // Check for exact matches of identifiers
                        var matchScore = 0f;
                        var matchedIdentifiers = new List<string>();
                        var contexts = new List<string>();
                        
                        foreach (var identifier in identifiers)
                        {
                            // Case-insensitive exact match
                            if (content.IndexOf(identifier, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                matchScore = 1.0f; // Perfect match
                                matchedIdentifiers.Add(identifier);
                                
                                // Extract context around the match
                                var contextList = ExtractContextAroundMatch(content, identifier, 200);
                                contexts.AddRange(contextList);
                                
                                _logger.LogInformation($"✅ EXACT MATCH found in {Path.GetFileName(filePath)}!");
                                break; // Found exact match, this is the winner
                            }
                        }
                        
                        // Also check for partial matches (name without GUID)
                        if (matchScore == 0)
                        {
                            var nameOnly = ExtractNameFromQuery(query);
                            if (!string.IsNullOrEmpty(nameOnly) && 
                                content.IndexOf(nameOnly, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                var partialContexts = ExtractContextAroundMatch(content, nameOnly, 200);
                                
                                lock (lockObj)
                                {
                                    results.Add((
                                        filePath,
                                        0.7f, // Partial match
                                        partialContexts,
                                        "partial_name"
                                    ));
                                }
                            }
                        }
                        else
                        {
                            // Add exact match result
                            lock (lockObj)
                            {
                                results.Add((
                                    filePath,
                                    matchScore,
                                    contexts,
                                    "exact_identifier"
                                ));
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error processing {filePath}: {ex.Message}");
                    }
                });
            });
            
            _logger.LogInformation($"Processed {processedFiles} files, found {results.Count} matches");
            return results.OrderByDescending(r => r.similarity).ToList();
        }
        
        private async Task<List<(string filePath, float similarity, List<string> relevantChunks, string documentType)>> PerformHybridSearch(
            string query, 
            List<string> filePaths,
            bool prioritizeExact)
        {
            _logger.LogInformation($"🔄 Performing HYBRID search (prioritizeExact={prioritizeExact})");
            
            var results = new Dictionary<string, HybridSearchResult>();
            
            // Phase 1: Keyword/Exact matching
            var keywordTask = Task.Run(() => PerformKeywordSearch(query, filePaths));
            
            // Phase 2: Semantic matching (if available)
            var semanticTask = Task.Run(() => PerformSemanticSearch(query, filePaths));
            
            await Task.WhenAll(keywordTask, semanticTask);
            
            var keywordResults = keywordTask.Result;
            var semanticResults = semanticTask.Result;
            
            // Merge results with intelligent scoring
            foreach (var kwResult in keywordResults)
            {
                results[kwResult.filePath] = new HybridSearchResult
                {
                    FilePath = kwResult.filePath,
                    FileName = Path.GetFileName(kwResult.filePath),
                    KeywordScore = kwResult.similarity,
                    MatchType = kwResult.documentType,
                    Snippets = kwResult.relevantChunks
                };
            }
            
            foreach (var semResult in semanticResults)
            {
                if (results.ContainsKey(semResult.filePath))
                {
                    results[semResult.filePath].SemanticScore = semResult.similarity;
                }
                else
                {
                    results[semResult.filePath] = new HybridSearchResult
                    {
                        FilePath = semResult.filePath,
                        FileName = Path.GetFileName(semResult.filePath),
                        SemanticScore = semResult.similarity,
                        MatchType = "semantic",
                        Snippets = semResult.relevantChunks
                    };
                }
            }
            
            // Calculate final scores with intelligent weighting
            var finalResults = new List<(string filePath, float similarity, List<string> relevantChunks, string documentType)>();
            
            foreach (var result in results.Values)
            {
                float finalScore;
                
                // CRITICAL: Boost exact matches to the top
                if (result.MatchType == "exact_match" || result.KeywordScore >= 1.0f)
                {
                    finalScore = 1.0f; // Guarantee exact matches are at the top
                    _logger.LogInformation($"🏆 EXACT MATCH BOOSTED: {result.FileName}");
                }
                else if (prioritizeExact && result.KeywordScore >= 0.8f)
                {
                    // Boost high keyword matches when looking for specific entities
                    finalScore = 0.85f + (result.KeywordScore * 0.15f);
                }
                else if (result.KeywordScore > 0 && result.SemanticScore > 0)
                {
                    // Both scores available - weighted combination
                    finalScore = (result.KeywordScore * 0.4f) + (result.SemanticScore * 0.6f);
                }
                else
                {
                    // Single score
                    finalScore = Math.Max(result.KeywordScore, result.SemanticScore) * 0.9f;
                }
                
                finalResults.Add((
                    result.FilePath,
                    finalScore,
                    result.Snippets,
                    result.MatchType
                ));
            }
            
            return finalResults.OrderByDescending(r => r.similarity).ToList();
        }
        
        private List<(string filePath, float similarity, List<string> relevantChunks, string documentType)> PerformKeywordSearch(string query, List<string> filePaths)
        {
            var results = new List<(string filePath, float similarity, List<string> relevantChunks, string documentType)>();
            var keywords = ExtractKeywords(query);
            
            // Check for exact identifier patterns
            var hasExactPattern = HasExactIdentifierPattern(query);
            
            foreach (var filePath in filePaths)
            {
                try
                {
                    var content = ExtractFileContent(filePath);
                    if (string.IsNullOrEmpty(content)) continue;
                    
                    var contentLower = content.ToLower();
                    var matchedKeywords = new List<string>();
                    float score = 0;
                    string matchType = "keyword_match";
                    
                    // Check for EXACT query match first (highest priority)
                    if (content.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        score = 1.0f; // Perfect exact match
                        matchType = "exact_match";
                        matchedKeywords.Add(query);
                        
                        var exactContext = ExtractContextAroundMatch(content, query, 300);
                        results.Add((filePath, score, exactContext, matchType));
                        _logger.LogInformation($"🎯 EXACT QUERY MATCH: {Path.GetFileName(filePath)}");
                        continue;
                    }
                    
                    // Check each keyword
                    foreach (var keyword in keywords)
                    {
                        if (contentLower.Contains(keyword.ToLower()))
                        {
                            matchedKeywords.Add(keyword);
                            // Longer keywords are more significant
                            score += keyword.Length > 5 ? 0.3f : 0.2f;
                        }
                    }
                    
                    if (matchedKeywords.Count > 0)
                    {
                        score = Math.Min(1.0f, score * (matchedKeywords.Count / (float)keywords.Count));
                        
                        // Extract context for matched keywords
                        var contexts = new List<string>();
                        foreach (var keyword in matchedKeywords.Take(3))
                        {
                            contexts.AddRange(ExtractContextAroundMatch(content, keyword, 200));
                        }
                        
                        results.Add((filePath, score, contexts, matchType));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error in keyword search for {filePath}: {ex.Message}");
                }
            }
            
            return results;
        }
        
        private List<(string filePath, float similarity, List<string> relevantChunks, string documentType)> PerformSemanticSearch(string query, List<string> filePaths)
        {
            try
            {
                // Use the existing SemanticSearch method from LuceneInterface
                var luceneResults = _luceneInterface.SemanticSearch(query, filePaths, 50);
                
                return luceneResults.Select(r => (
                    r.FilePath ?? r.FileName ?? "",
                    r.Score * 0.8f, // Slightly reduce semantic scores to favor exact matches
                    r.Snippets ?? new List<string>(),
                    "semantic"
                )).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error in semantic search: {ex.Message}");
                return new List<(string filePath, float similarity, List<string> relevantChunks, string documentType)>();
            }
        }
        
        private List<string> ExtractContextAroundMatch(string content, string match, int contextLength)
        {
            var contexts = new List<string>();
            var index = 0;
            
            while ((index = content.IndexOf(match, index, StringComparison.OrdinalIgnoreCase)) != -1)
            {
                var start = Math.Max(0, index - contextLength / 2);
                var length = Math.Min(contextLength, content.Length - start);
                var context = content.Substring(start, length);
                
                // Clean up context
                if (start > 0) context = "..." + context;
                if (start + length < content.Length) context += "...";
                
                contexts.Add(context.Trim());
                index += match.Length;
                
                if (contexts.Count >= 3) break; // Limit to 3 contexts
            }
            
            return contexts;
        }
        
        private string ExtractNameFromQuery(string query)
        {
            // Remove GUID parts to get just the name
            var guidPattern = @"-[a-fA-F0-9]{8,}";
            return Regex.Replace(query, guidPattern, "").Trim();
        }
        
        private List<string> ExtractKeywords(string query)
        {
            var stopWords = new HashSet<string> 
            { 
                "the", "is", "at", "which", "on", "a", "an", "and", "or", "but",
                "in", "with", "to", "for", "of", "as", "from", "by", "about",
                "what", "where", "when", "how", "why", "who", "this", "that"
            };
            
            return query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => !stopWords.Contains(w.ToLower()) && w.Length > 2)
                .Distinct()
                .ToList();
        }
        
        private bool HasExactIdentifierPattern(string query)
        {
            // Check for GUID patterns, codes, etc.
            var patterns = new[]
            {
                @"[a-fA-F0-9]{8,}(?:-[a-fA-F0-9]{4,})*", // GUIDs
                @"\b[A-Z0-9]{6,}\b", // Codes
                @"\b\d{6,}\b" // Long numbers
            };
            
            return patterns.Any(pattern => Regex.IsMatch(query, pattern));
        }
        
        private string ExtractFileContent(string filePath)
        {
            return FileTextExtractor.ExtractTextFromFile(filePath);
        }
    }

    // Supporting classes
    public class QueryAnalysis
    {
        public string OriginalQuery { get; set; } = "";
        public QueryIntentType Type { get; set; }
        public bool HasExactIdentifier { get; set; }
        public List<string> Identifiers { get; set; } = new();
        public List<string> NamedEntities { get; set; } = new();
    }

    public enum QueryIntentType
    {
        ExactIdentifierSearch,  // Looking for specific GUIDs/IDs
        NavigationalSearch,     // Looking for specific named entities
        InformationalSearch     // General information queries
    }

    public class HybridSearchResult
    {
        public string FilePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public float KeywordScore { get; set; }
        public float SemanticScore { get; set; }
        public string MatchType { get; set; } = "";
        public List<string> Snippets { get; set; } = new();
    }
}