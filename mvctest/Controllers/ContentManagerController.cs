using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using mvctest.Models;
using mvctest.Services;
using System.IO.Compression;
using System.Text;

namespace mvctest.Controllers
{
    public partial class ContentManagerController : Controller
    {
        private readonly ILuceneInterface _luceneInterface;
        private readonly IContentManager _contentManager;
        private readonly IChatMLService _chatMLService;
        private readonly AppSettings _appSettings;

        public ContentManagerController(IContentManager contentManager, ILuceneInterface luceneInterface, IChatMLService chatMLService, IOptions<AppSettings> appSettings)
        {
            _contentManager = contentManager;
            _luceneInterface = luceneInterface;
            _chatMLService = chatMLService;
            _appSettings = appSettings.Value;

            // Initialize ML.NET Advanced Search Service
            var indexPath = Path.Combine("C:\\Users\\ukhan2\\Desktop\\", "ml_advanced_index");
            var modelPath = Path.Combine("C:\\Users\\ukhan2\\Desktop\\ONNXModel", "model.onnx");

            Console.WriteLine("✅ ContentManagerController initialized with ML.NET Advanced Search Service");
        }

        public IActionResult Index(int page = 1, int pageSize = 10)
        {
            try
            {
                // Get paginated records directly from backend
                var paginatedResult = _contentManager.GetPaginatedRecords("*", page, pageSize);

                var viewModel = new PaginatedRecordViewModel
                {
                    Records = paginatedResult.Records,
                    CurrentPage = page,
                    PageSize = pageSize,
                    TotalRecords = paginatedResult.TotalRecords
                };

                return View(viewModel);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Failed to Record List Because {ex.Message}";
                return RedirectToAction("Error", "Home", new { message = ex.Message });
            }
        }

        public IActionResult Create()
        {
            return View();
        }

        public IActionResult CreateRecord(CreateRecord record)
        {
            try
            {
                var rec = _contentManager.CreateRecord(record);
                return View(rec);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Failed to Create Record  Because {ex.Message}";
                return RedirectToAction("Error", "Home", new { message = ex.Message });
            }
        }

        public IActionResult Details(int id)
        {
            try
            {
                var recordDetails = _contentManager.GetRecordwithURI(id);
                return View(recordDetails);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Failed to Load Details Because {ex.Message}";
                return RedirectToAction("Error", "Home", new { message = ex.Message });
            }
        }

        public IActionResult Download(int id)
        {
            try
            {
                var fileBytes = _contentManager.Download(id);
                return File(fileBytes.File, "application/octet-stream", fileBytes.FileName);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Failed to Download Because {ex.Message}";
                return RedirectToAction("Error", "Home", new { message = ex.Message });
            }
        }

        [HttpPost]
        public IActionResult DownloadSelected(List<int> selectedIds)
        {
            try
            {
                if (selectedIds == null || !selectedIds.Any())
                    return RedirectToAction("Index");

                // Combine files into ZIP
                using (var memoryStream = new MemoryStream())
                {
                    using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, true))
                    {
                        foreach (var id in selectedIds)
                        {
                            var file = _contentManager.Download(id);
                            var zipEntry = archive.CreateEntry(file.FileName);

                            using (var entryStream = zipEntry.Open())
                            using (var fileStream = new MemoryStream(file.File))
                            {
                                fileStream.CopyTo(entryStream);
                            }
                        }
                    }

                    return File(memoryStream.ToArray(), "application/zip", "SelectedDocuments.zip");
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Download failed: " + ex.Message;
                return RedirectToAction("Error", "Home");
            }
        }

        [HttpGet]
        public IActionResult Delete(int id)
        {
            try
            {
                var model = _contentManager.GetRecordwithURI(id);
                if (model == null)
                    return NotFound();

                return View(model);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Failed to Delete Because {ex.Message}";
                return RedirectToAction("Error", "Home", new { message = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteConfirmed(int id)
        {
            try
            {
                var success = _contentManager.DeleteRecord(id);

                if (success)
                    return RedirectToAction("Index");
                else
                    return BadRequest("Unable to delete record.");
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Failed to DeleteRecord Because {ex.Message}";
                return RedirectToAction("Error", "Home", new { message = ex.Message });
            }
        }

        [HttpGet]
        public IActionResult IndexSearch()
        {
            try
            {
                // Clear any large TempData that might cause HTTP 431 errors
                TempData.Remove("EnhancedResults");
                TempData.Remove("QueryId");
                
                // Keep only essential messages but clear them after use
                var successMessage = TempData["SuccessMessage"];
                var errorMessage = TempData["ErrorMessage"];
                var infoMessage = TempData["InfoMessage"];
                
                // Clear all TempData to prevent header size issues
                TempData.Clear();
                
                // Restore only essential messages
                if (successMessage != null)
                    TempData["SuccessMessage"] = successMessage;
                if (errorMessage != null)
                    TempData["ErrorMessage"] = errorMessage;
                if (infoMessage != null)
                    TempData["InfoMessage"] = infoMessage;
                
                return View();
            }
            catch (Exception ex)
            {
                TempData.Clear(); // Clear everything on error
                TempData["ErrorMessage"] = $"Search page loading failed: {ex.Message}";
                return RedirectToAction("Error", "Home", new { message = ex.Message });
            }
        }

        public async Task<IActionResult> SearchResults(string content,
            SearchMode mode = SearchMode.Comprehensive,
            SearchResultSort sortBy = SearchResultSort.Relevance,
            string? fileType = null,
            bool showWordAnalysis = false,
            bool showSentenceContext = false,
            int maxResults = 50)
        {
            var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var performance = new SearchPerformanceMetrics();

            try
            {
                Console.WriteLine($"🔍 Enhanced Multi-Level Search: '{content}' | Mode: {mode} | Sort: {sortBy}");

                if (string.IsNullOrWhiteSpace(content))
                {
                    TempData["InfoMessage"] = "Please enter a search query.";
                    return View("SearchResults", new List<SearchResultModel>());
                }

                var searchParams = new AdvancedSearchParameters
                {
                    Query = content,
                    Mode = mode,
                    SortBy = sortBy,
                    FileType = fileType,
                    MaxResults = maxResults,
                    ShowWordAnalysis = showWordAnalysis,
                    ShowSentenceContext = showSentenceContext
                };

                var enhancedResults = await PerformEnhancedSearch(searchParams, performance);

                enhancedResults.Performance = performance;
                enhancedResults.UsedMode = mode;
                enhancedResults.Query = content;

                // Group results by file path to avoid duplicates, combine content matches
                var allResults = enhancedResults.DocumentResults
                    .Concat(enhancedResults.WordResults)
                    .Concat(enhancedResults.SentenceResults)
                    .ToList();

                Console.WriteLine($"DEBUG: Total results before grouping: {allResults.Count}");
                Console.WriteLine($"DEBUG: Document results: {enhancedResults.DocumentResults.Count}");
                Console.WriteLine($"DEBUG: Word results: {enhancedResults.WordResults.Count}");
                Console.WriteLine($"DEBUG: Sentence results: {enhancedResults.SentenceResults.Count}");

                var groupedResults = allResults
                    .Where(r => r != null && !string.IsNullOrEmpty(r.FilePath)) // Filter out null results
                    .GroupBy(r => r.FilePath)
                    .Select(group =>
                    {
                        var primaryResult = group.OrderByDescending(r => r.Score).FirstOrDefault();
                        
                        // Skip if primaryResult is null
                        if (primaryResult == null)
                        {
                            Console.WriteLine("Warning: primaryResult is null for a group");
                            return null;
                        }

                        // Combine all snippets from all matches of the same file and extract complete sentences
                        var allSnippets = group
                            .Where(r => r.Snippets != null && r.Snippets.Any())
                            .SelectMany(r => r.Snippets)
                            .Distinct()
                            .Select(snippet => ExtractCompleteSentence(snippet, content)) // Extract complete sentences based on full stops
                            .Where(snippet => !string.IsNullOrWhiteSpace(snippet)) // Filter out null/empty results
                            .Take(5) // Limit to maximum 5 snippets per file
                            .ToList();

                        // Create consolidated result
                        var consolidatedResult = new SearchResultModel
                        {
                            FileName = primaryResult.FileName,
                            FilePath = primaryResult.FilePath,
                            Content = primaryResult.Content,
                            Score = group.Max(r => r.Score), // Use highest score
                            date = primaryResult.date,
                            Metadata = primaryResult.Metadata ?? new Dictionary<string, string>(),
                            Snippets = allSnippets,
                            EntityMatches = primaryResult.EntityMatches ?? new List<string>(),
                            SemanticSimilarity = primaryResult.SemanticSimilarity,
                            Confidence = primaryResult.Confidence,
                            MLFeatures = primaryResult.MLFeatures ?? new Dictionary<string, float>()
                        };

                        // Add match type information to metadata
                        var matchTypes = new List<string>();
                        if (group.Any(r => r.Metadata?.GetValueOrDefault("doc_type") == "word"))
                            matchTypes.Add("Word-level");
                        if (group.Any(r => r.Metadata?.GetValueOrDefault("doc_type") == "sentence"))
                            matchTypes.Add("Sentence-level");
                        if (group.Any(r => r.Metadata?.ContainsKey("doc_type") != true || r.Metadata["doc_type"] == "document"))
                            matchTypes.Add("Document-level");

                        if (matchTypes.Any())
                        {
                            consolidatedResult.Metadata["SearchLevels"] = string.Join(", ", matchTypes);
                            consolidatedResult.Metadata["TotalMatches"] = group.Count().ToString();
                        }

                        return consolidatedResult;
                    })
                    .Where(r => r != null) // Filter out null results
                    .OrderByDescending(r => r.Score)
                    .ToList();

                Console.WriteLine($"DEBUG: Final grouped results count: {groupedResults.Count}");
                for (int i = 0; i < Math.Min(3, groupedResults.Count); i++)
                {
                    var result = groupedResults[i];
                    Console.WriteLine($"DEBUG: Result {i + 1}: FileName='{result.FileName}', FilePath='{result.FilePath}', Content length={result.Content?.Length ?? 0}, Snippets count={result.Snippets?.Count ?? 0}");
                    if (result.Snippets != null && result.Snippets.Any())
                    {
                        Console.WriteLine($"DEBUG: First snippet: '{result.Snippets.First()}'");
                    }
                }

                totalStopwatch.Stop();
                performance.TotalSearchTime = totalStopwatch.Elapsed;

                if (groupedResults.Any())
                {
                    var uniqueFileCount = groupedResults.Count;
                    var totalMatches = enhancedResults.TotalResults;

                    TempData["SuccessMessage"] = $"Found {uniqueFileCount} files ({totalMatches} matches) in {performance.TotalSearchTime.TotalMilliseconds:F0}ms";
                    // Simplified QueryId to prevent header size issues
                    TempData["QueryId"] = DateTime.Now.Ticks.ToString();

                    Console.WriteLine($"✅ Enhanced search completed: {uniqueFileCount} unique files, {totalMatches} total matches, {performance.TotalSearchTime.TotalMilliseconds:F0}ms");
                    Console.WriteLine($"📊 Performance breakdown: Documents={performance.DocumentMatchesFound}, Words={performance.WordMatchesFound}, Sentences={performance.SentenceMatchesFound}");
                }
                else
                {
                    TempData["InfoMessage"] = $"No search results found for '{content}' using {mode} mode. Try a different search mode or query.";
                    Console.WriteLine($"❌ No results found with {mode} mode");
                }

                // Don't store enhanced results in TempData to prevent HTTP 431 errors
                // TempData["EnhancedResults"] = System.Text.Json.JsonSerializer.Serialize(enhancedResults);

                return View("SearchResults", groupedResults);
            }
            catch (Exception ex)
            {
                totalStopwatch.Stop();
                Console.WriteLine($"❌ Enhanced search error after {totalStopwatch.ElapsedMilliseconds}ms: {ex.Message}");
                TempData["ErrorMessage"] = $"Search failed: {ex.Message}";
                return View("SearchResults", new List<SearchResultModel>());
            }
        }

        [HttpPost]
        public async Task<IActionResult> GetFileSummary([FromBody] FileSummaryRequest request)
        {
            try
            {
                // Debug logging
                Console.WriteLine($"GetFileSummary called with request: {request?.FilePath}");

                if (request == null)
                {
                    Console.WriteLine("Request is null");
                    return BadRequest(new { error = "Request is null" });
                }

                if (string.IsNullOrEmpty(request.FilePath))
                {
                    Console.WriteLine("FilePath is null or empty");
                    return BadRequest(new { error = "File path is required" });
                }

                Console.WriteLine($"Calling ChatML service for file: {request.FilePath}");
                var summary = await _chatMLService.GetFileSummaryAsync(request.FilePath);
                Console.WriteLine($"Summary generated: {summary?.Length} characters");

                return Json(new { summary = summary });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetFileSummary: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                TempData["ErrorMessage"] = $"Error in GetFileSummary: {ex.Message}";
                return RedirectToAction("Error", "Home", new { message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> GetFileSummaryWithSimulatedStreaming([FromBody] FileSummaryRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.FilePath))
                {
                    return BadRequest(new { error = "File path is required" });
                }

                var summary = await _chatMLService.GetFileSummaryAsync(request.FilePath);
                return Json(new { summary = summary });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = $"Failed to generate summary: {ex.Message}" });
            }
        }

        [HttpPost]
        public async Task<IActionResult> DeepContentSearch([FromBody] DeepSearchRequest request)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (string.IsNullOrEmpty(request.Query))
                {
                    return BadRequest(new { error = "Query is required" });
                }

                if (request.FilePaths == null || !request.FilePaths.Any())
                {
                    return BadRequest(new { error = "File paths are required" });
                }

                Console.WriteLine($"🚀 DeepContentSearch: Using Intelligent Search for query: '{request.Query}'");
                Console.WriteLine($"Processing {request.FilePaths.Count} specific files");
                Console.WriteLine($"⏱️ DeepContentSearch started at: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");

                var searchTypes = new List<string> { "Intelligent Search + AI Analysis" };

                var searchStartTime = stopwatch.Elapsed;
                Console.WriteLine($"⏱️ Starting Enhanced Multi-Level search at: {searchStartTime.TotalSeconds:F2} seconds");

                // Use enhanced search with intelligent mode detection
                var searchMode = DetectOptimalSearchMode(request.Query);
                Console.WriteLine($"🎯 Detected optimal search mode: {searchMode}");

                var searchParams = new AdvancedSearchParameters
                {
                    Query = request.Query,
                    Mode = searchMode,
                    MaxResults = 20,
                    ShowWordAnalysis = searchMode == SearchMode.WordLevel,
                    ShowSentenceContext = searchMode == SearchMode.SentenceLevel || searchMode == SearchMode.Hybrid
                };

                var performance = new SearchPerformanceMetrics();
                var enhancedResults = await PerformEnhancedPathSearch(searchParams, request.FilePaths, performance,true);

                // Convert to the expected format
                var searchResults = enhancedResults.DocumentResults
                    .Concat(enhancedResults.WordResults)
                    .Concat(enhancedResults.SentenceResults)
                    //.OrderByDescending(r => r.Score)
                    .ToList();

                var searchEndTime = stopwatch.Elapsed;
                Console.WriteLine($"⏱️ Enhanced search completed at: {searchEndTime.TotalSeconds:F2} seconds (took {(searchEndTime - searchStartTime).TotalSeconds:F2} seconds)");
                Console.WriteLine($"📊 Multi-level results: Documents={enhancedResults.DocumentResults.Count}, Words={enhancedResults.WordResults.Count}, Sentences={enhancedResults.SentenceResults.Count}");

                if (!searchResults.Any())
                {
                    Console.WriteLine("No search results found");
                    return Json(new
                    {
                        answer = $"I couldn't find any information related to '{request.Query}' in the provided documents. The content may not be relevant to your question.",
                        searchMetadata = new
                        {
                            searchTypes = searchTypes,
                            resultsFound = 0,
                            highResolutionEnabled = true,
                            queryType = "Lucene Search",
                            filesSearched = request.FilePaths.Count,
                            filesWithMatches = 0,
                            averageScore = 0f,
                            topScore = 0f,
                            relevantFiles = new List<object>()
                        }
                    });
                }

                Console.WriteLine($"Search found {searchResults.Count} relevant results");

                // Use search results ranking
                var topSearchResults = searchResults.Take(5).ToList();
                var detailedResults = new List<(string filePath, float relevanceScore, string relevantContent)>();

                foreach (var result in topSearchResults)
                {
                    try
                    {
                        Console.WriteLine($"Detailed analysis of {System.IO.Path.GetFileName(result.FilePath)} (search score: {result.Score:F3})");

                        // Extract full content for detailed analysis
                        var fullContent = Services.FileTextExtractor.ExtractTextFromFile(result.FilePath);
                        if (!string.IsNullOrEmpty(fullContent))
                        {
                            // Use the search score as relevance score
                            detailedResults.Add((result.FilePath, result.Score, fullContent));
                            Console.WriteLine($"✓ Added {System.IO.Path.GetFileName(result.FilePath)} for detailed processing");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error processing {result.FilePath}: {ex.Message}");
                    }
                }

                // Declare timing variables outside the if blocks
                var aiStartTime = stopwatch.Elapsed;
                var aiEndTime = stopwatch.Elapsed;

                string enhancedAnswer;
                if (detailedResults.Any())
                {
                    Console.WriteLine($"🚀 SMART AI Generation: Processing up to {detailedResults.Count} files with early termination");

                    aiStartTime = stopwatch.Elapsed;
                    Console.WriteLine($"⏱️ Starting AI generation at: {aiStartTime.TotalSeconds:F2} seconds");

                    // 🎯 SMART EARLY TERMINATION LOGIC
                    enhancedAnswer = await GenerateAnswerWithEarlyTermination(request.Query, detailedResults);

                    aiEndTime = stopwatch.Elapsed;
                    Console.WriteLine($"⏱️ AI generation completed at: {aiEndTime.TotalSeconds:F2} seconds (took {(aiEndTime - aiStartTime).TotalSeconds:F2} seconds)");
                }
                else
                {
                    Console.WriteLine("No files passed detailed analysis");
                    enhancedAnswer = $"I couldn't extract detailed information related to '{request.Query}' from the semantically relevant files.";
                }

                // Highlight search terms in the AI response
                var queryWords = request.Query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var word in queryWords)
                {
                    var regex = new System.Text.RegularExpressions.Regex($@"\b{System.Text.RegularExpressions.Regex.Escape(word)}\b",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    enhancedAnswer = regex.Replace(enhancedAnswer, $"<strong>$0</strong>");
                }

                stopwatch.Stop();
                var totalTimeMinutes = stopwatch.Elapsed.TotalMinutes;
                var totalTimeSeconds = stopwatch.Elapsed.TotalSeconds;

                Console.WriteLine($"⏱️ DeepContentSearch COMPLETED:");
                Console.WriteLine($"   📊 Total Time: {totalTimeMinutes:F2} minutes ({totalTimeSeconds:F2} seconds)");
                Console.WriteLine($"   📁 Files Processed: {request.FilePaths.Count}");
                Console.WriteLine($"   🔍 Search Matches: {searchResults.Count}");
                Console.WriteLine($"   🧠 AI Analysis Files: {detailedResults.Count}");
                Console.WriteLine($"   ✅ Completed at: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");

                return Json(new
                {
                    answer = enhancedAnswer,
                    searchMetadata = new
                    {
                        searchTypes = searchTypes,
                        resultsFound = detailedResults.Count,
                        highResolutionEnabled = true,
                        queryType = "Lucene Search + AI Analysis",
                        filesSearched = request.FilePaths.Count,
                        searchMatches = searchResults.Count,
                        filesWithDetailedAnalysis = detailedResults.Count,
                        averageScore = searchResults.Any() ? searchResults.Average(r => r.Score) : 0f,
                        topScore = searchResults.Any() ? searchResults.Max(r => r.Score) : 0f,
                        totalTimeMinutes = totalTimeMinutes,
                        totalTimeSeconds = totalTimeSeconds,
                        performanceBreakdown = new
                        {
                            searchTimeSeconds = searchResults.Any() ? (searchEndTime - searchStartTime).TotalSeconds : 0,
                            aiGenerationTimeSeconds = detailedResults.Any() ? (aiEndTime - aiStartTime).TotalSeconds : 0
                        },
                        relevantFiles = searchResults.Take(5).Select(r => new
                        {
                            fileName = System.IO.Path.GetFileName(r.FilePath),
                            relevanceScore = r.Score,
                            documentType = "Lucene Result"
                        }).ToList()
                    }
                });
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                var errorTimeMinutes = stopwatch.Elapsed.TotalMinutes;
                var errorTimeSeconds = stopwatch.Elapsed.TotalSeconds;

                Console.WriteLine($"❌ DeepContentSearch ERROR after {errorTimeMinutes:F2} minutes ({errorTimeSeconds:F2} seconds):");
                Console.WriteLine($"   Error: {ex.Message}");
                Console.WriteLine($"   StackTrace: {ex.StackTrace}");

                TempData["ErrorMessage"] = $"Failed to perform optimized content search: {ex.Message}";
                return RedirectToAction("Error", "Home", new { message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> AIAnalytics([FromBody] DeepSearchRequest request)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (string.IsNullOrEmpty(request.Query))
                {
                    return BadRequest(new { error = "Query is required" });
                }

                if (request.FilePaths == null || !request.FilePaths.Any())
                {
                    return BadRequest(new { error = "File paths are required" });
                }

                Console.WriteLine($"🚀 DeepContentSearch: Using Intelligent Search for query: '{request.Query}'");
                Console.WriteLine($"Processing {request.FilePaths.Count} specific files");
                Console.WriteLine($"⏱️ DeepContentSearch started at: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");

                var searchTypes = new List<string> { "Intelligent Search + AI Analysis" };

                var searchStartTime = stopwatch.Elapsed;
                Console.WriteLine($"⏱️ Starting Enhanced Multi-Level search at: {searchStartTime.TotalSeconds:F2} seconds");

                // Use enhanced search with intelligent mode detection
                var searchMode = DetectOptimalSearchMode(request.Query);
                Console.WriteLine($"🎯 Detected optimal search mode: {searchMode}");

                var searchParams = new AdvancedSearchParameters
                {
                    Query = request.Query,
                    Mode = searchMode,
                    MaxResults = 20,
                    ShowWordAnalysis = searchMode == SearchMode.WordLevel,
                    ShowSentenceContext = searchMode == SearchMode.SentenceLevel || searchMode == SearchMode.Hybrid
                };

                var performance = new SearchPerformanceMetrics();
                var enhancedResults = await PerformEnhancedPathSearch(searchParams, request.FilePaths, performance,request.FromAnalytics);

                // Convert to the expected format
                var searchResults = enhancedResults.DocumentResults
                    .Concat(enhancedResults.WordResults)
                    .Concat(enhancedResults.SentenceResults)
                    //.OrderByDescending(r => r.Score)
                    .ToList();

                var searchEndTime = stopwatch.Elapsed;
                Console.WriteLine($"⏱️ Enhanced search completed at: {searchEndTime.TotalSeconds:F2} seconds (took {(searchEndTime - searchStartTime).TotalSeconds:F2} seconds)");
                Console.WriteLine($"📊 Multi-level results: Documents={enhancedResults.DocumentResults.Count}, Words={enhancedResults.WordResults.Count}, Sentences={enhancedResults.SentenceResults.Count}");

                if (!searchResults.Any())
                {
                    Console.WriteLine("No search results found");
                    return Json(new
                    {
                        answer = $"I couldn't find any information related to '{request.Query}' in the provided documents. The content may not be relevant to your question.",
                        searchMetadata = new
                        {
                            searchTypes = searchTypes,
                            resultsFound = 0,
                            highResolutionEnabled = true,
                            queryType = "Lucene Search",
                            filesSearched = request.FilePaths.Count,
                            filesWithMatches = 0,
                            averageScore = 0f,
                            topScore = 0f,
                            relevantFiles = new List<object>()
                        }
                    });
                }

                Console.WriteLine($"Search found {searchResults.Count} relevant results");

                // Use search results ranking
                var topSearchResults = searchResults.Take(5).ToList();
                var detailedResults = new List<(string filePath, float relevanceScore, string relevantContent)>();

                foreach (var result in topSearchResults)
                {
                    try
                    {
                        Console.WriteLine($"Detailed analysis of {System.IO.Path.GetFileName(result.FilePath)} (search score: {result.Score:F3})");

                        // Extract full content for detailed analysis
                        var fullContent = Services.FileTextExtractor.ExtractTextFromFile(result.FilePath);
                        if (!string.IsNullOrEmpty(fullContent))
                        {
                            // Use the search score as relevance score
                            detailedResults.Add((result.FilePath, result.Score, fullContent));
                            Console.WriteLine($"✓ Added {System.IO.Path.GetFileName(result.FilePath)} for detailed processing");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error processing {result.FilePath}: {ex.Message}");
                    }
                }

                // Declare timing variables outside the if blocks
                var aiStartTime = stopwatch.Elapsed;
                var aiEndTime = stopwatch.Elapsed;

                string enhancedAnswer;
                if (detailedResults.Any())
                {
                    Console.WriteLine($"🚀 SMART AI Generation: Processing up to {detailedResults.Count} files with early termination");

                    aiStartTime = stopwatch.Elapsed;
                    Console.WriteLine($"⏱️ Starting AI generation at: {aiStartTime.TotalSeconds:F2} seconds");

                    // 🎯 SMART EARLY TERMINATION LOGIC
                    enhancedAnswer = await GenerateAnswerWithEarlyTermination(request.Query, detailedResults);

                    aiEndTime = stopwatch.Elapsed;
                    Console.WriteLine($"⏱️ AI generation completed at: {aiEndTime.TotalSeconds:F2} seconds (took {(aiEndTime - aiStartTime).TotalSeconds:F2} seconds)");
                }
                else
                {
                    Console.WriteLine("No files passed detailed analysis");
                    enhancedAnswer = $"I couldn't extract detailed information related to '{request.Query}' from the semantically relevant files.";
                }

                // Highlight search terms in the AI response
                var queryWords = request.Query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var word in queryWords)
                {
                    var regex = new System.Text.RegularExpressions.Regex($@"\b{System.Text.RegularExpressions.Regex.Escape(word)}\b",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    enhancedAnswer = regex.Replace(enhancedAnswer, $"<strong>$0</strong>");
                }

                stopwatch.Stop();
                var totalTimeMinutes = stopwatch.Elapsed.TotalMinutes;
                var totalTimeSeconds = stopwatch.Elapsed.TotalSeconds;

                Console.WriteLine($"⏱️ DeepContentSearch COMPLETED:");
                Console.WriteLine($"   📊 Total Time: {totalTimeMinutes:F2} minutes ({totalTimeSeconds:F2} seconds)");
                Console.WriteLine($"   📁 Files Processed: {request.FilePaths.Count}");
                Console.WriteLine($"   🔍 Search Matches: {searchResults.Count}");
                Console.WriteLine($"   🧠 AI Analysis Files: {detailedResults.Count}");
                Console.WriteLine($"   ✅ Completed at: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");

                return Json(new
                {
                    answer = enhancedAnswer,
                    searchMetadata = new
                    {
                        searchTypes = searchTypes,
                        resultsFound = detailedResults.Count,
                        highResolutionEnabled = true,
                        queryType = "Lucene Search + AI Analysis",
                        filesSearched = request.FilePaths.Count,
                        searchMatches = searchResults.Count,
                        filesWithDetailedAnalysis = detailedResults.Count,
                        averageScore = searchResults.Any() ? searchResults.Average(r => r.Score) : 0f,
                        topScore = searchResults.Any() ? searchResults.Max(r => r.Score) : 0f,
                        totalTimeMinutes = totalTimeMinutes,
                        totalTimeSeconds = totalTimeSeconds,
                        performanceBreakdown = new
                        {
                            searchTimeSeconds = searchResults.Any() ? (searchEndTime - searchStartTime).TotalSeconds : 0,
                            aiGenerationTimeSeconds = detailedResults.Any() ? (aiEndTime - aiStartTime).TotalSeconds : 0
                        },
                        relevantFiles = searchResults.Take(5).Select(r => new
                        {
                            fileName = System.IO.Path.GetFileName(r.FilePath),
                            relevanceScore = r.Score,
                            documentType = "Lucene Result"
                        }).ToList()
                    }
                });
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                var errorTimeMinutes = stopwatch.Elapsed.TotalMinutes;
                var errorTimeSeconds = stopwatch.Elapsed.TotalSeconds;

                Console.WriteLine($"❌ DeepContentSearch ERROR after {errorTimeMinutes:F2} minutes ({errorTimeSeconds:F2} seconds):");
                Console.WriteLine($"   Error: {ex.Message}");
                Console.WriteLine($"   StackTrace: {ex.StackTrace}");

                TempData["ErrorMessage"] = $"Failed to perform optimized content search: {ex.Message}";
                return RedirectToAction("Error", "Home", new { message = ex.Message });
            }
        }

        private string ExtractCompleteSentence(string snippet, string searchQuery = "")
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

    }

    public class FileSummaryRequest
    {
        public string FilePath { get; set; } = string.Empty;
    }

    public class DeepSearchRequest
    {
        public string Query { get; set; } = string.Empty;
        public List<string> FilePaths { get; set; } = new List<string>();
        public string? QueryId { get; set; }
        public bool FromAnalytics { get; set; }
    }

    public enum SearchMode
    {
        Comprehensive,  // Default - searches all levels
        WordLevel,      // Word-by-word search
        SentenceLevel,  // Sentence-by-sentence search
        DocumentLevel,  // Main document content only
        Semantic,       // Semantic/embedding-based search
        Hybrid         // Combines multiple approaches
    }

    public enum SearchResultSort
    {
        Relevance,      // Default - by search score
        Date,           // By indexed date
        FileName,       // Alphabetical by filename
        FileSize,       // By file size
        WordFrequency,  // By word frequency (for word searches)
        SentenceIndex   // By sentence position (for sentence searches)
    }

    public class AdvancedSearchParameters
    {
        public string Query { get; set; } = string.Empty;
        public SearchMode Mode { get; set; } = SearchMode.Comprehensive;
        public SearchResultSort SortBy { get; set; } = SearchResultSort.Relevance;
        public string? FileType { get; set; }
        public DateTime? DateFrom { get; set; }
        public DateTime? DateTo { get; set; }
        public int? MinWordCount { get; set; }
        public int? MaxResults { get; set; } = 50;
        public bool IncludeContext { get; set; } = true;
        public bool ShowWordAnalysis { get; set; } = false;
        public bool ShowSentenceContext { get; set; } = false;
    }

    public class EnhancedSearchResultsViewModel
    {
        public List<SearchResultModel> DocumentResults { get; set; } = new List<SearchResultModel>();
        public List<SearchResultModel> WordResults { get; set; } = new List<SearchResultModel>();
        public List<SearchResultModel> SentenceResults { get; set; } = new List<SearchResultModel>();
        public SearchPerformanceMetrics Performance { get; set; } = new SearchPerformanceMetrics();
        public SearchMode UsedMode { get; set; }
        public string Query { get; set; } = string.Empty;
        public int TotalResults { get; set; }
    }

    public class SearchPerformanceMetrics
    {
        public TimeSpan TotalSearchTime { get; set; }
        public TimeSpan WordSearchTime { get; set; }
        public TimeSpan SentenceSearchTime { get; set; }
        public TimeSpan DocumentSearchTime { get; set; }
        public TimeSpan SemanticSearchTime { get; set; }
        public int TotalDocumentsSearched { get; set; }
        public int WordMatchesFound { get; set; }
        public int SentenceMatchesFound { get; set; }
        public int DocumentMatchesFound { get; set; }
    }

    public class WordAnalysisData
    {
        public string Word { get; set; } = string.Empty;
        public int Frequency { get; set; }
        public List<int> Positions { get; set; } = new List<int>();
        public string Context { get; set; } = string.Empty;
        public int FirstPosition { get; set; }
    }

    public class SentenceContextData
    {
        public string Sentence { get; set; } = string.Empty;
        public int SentenceIndex { get; set; }
        public string? PreviousSentence { get; set; }
        public string? NextSentence { get; set; }
        public string ParentFile { get; set; } = string.Empty;
    }

    public class ClassifyDocumentRequest
    {
        public string FilePath { get; set; } = string.Empty;
    }
}