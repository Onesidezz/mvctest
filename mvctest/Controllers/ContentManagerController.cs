using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using mvctest.Models;
using mvctest.Services;
using System.IO.Compression;

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
                return View();
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Failed to IndexSearch Because {ex.Message}";
                return RedirectToAction("Error", "Home", new { message = ex.Message });
            }
        }

        public async Task<IActionResult> SearchResults(string content)
        {
            try
            {
                Console.WriteLine($"🔍 Phase 1 Search (Broad): '{content}'");

                if (string.IsNullOrWhiteSpace(content))
                {
                    TempData["InfoMessage"] = "Please enter a search query.";
                    return View(new List<Models.SearchResultModel>());
                }

                // Use direct Lucene search instead of two-phase approach
                var searchResults = _luceneInterface.SearchFiles(content) ?? new List<Models.SearchResultModel>();
                var resultSet = new { Results = searchResults, SearchTimeMs = 0, QueryId = Guid.NewGuid().ToString(), Metadata = new { CacheHit = false } };

                if (resultSet.Results.Any())
                {
                    TempData["SuccessMessage"] = $"Found {resultSet.Results.Count} results in {resultSet.SearchTimeMs}ms (cached: {resultSet.Metadata.CacheHit})";
                    TempData["QueryId"] = resultSet.QueryId;

                    Console.WriteLine($"✅ Phase 1 completed: {resultSet.Results.Count} results, {resultSet.SearchTimeMs}ms, QueryId: {resultSet.QueryId}");
                }
                else
                {
                    TempData["InfoMessage"] = "No search results found. The index might be empty or the query didn't match any documents.";
                    Console.WriteLine("❌ No results found in Phase 1 search");
                }

                return View(resultSet.Results);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Phase 1 Search error: {ex.Message}");
                TempData["ErrorMessage"] = $"Search failed: {ex.Message}";
                return View(new List<Models.SearchResultModel>());
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

                // Use the intelligent search orchestrator directly
                Console.WriteLine("🧠 Using intelligent semantic search with early termination");

                var searchStartTime = stopwatch.Elapsed;
                Console.WriteLine($"⏱️ Starting Lucene search at: {searchStartTime.TotalSeconds:F2} seconds");
                
                var searchResults = _luceneInterface.SearchFilesInPaths(request.Query,request.FilePaths) ?? new List<Models.SearchResultModel>();
                
                var searchEndTime = stopwatch.Elapsed;
                Console.WriteLine($"⏱️ Lucene search completed at: {searchEndTime.TotalSeconds:F2} seconds (took {(searchEndTime - searchStartTime).TotalSeconds:F2} seconds)");

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

        private async Task<string> GenerateAIAnswer(string query, List<SearchResultModel> results)
        {
            try
            {
                if (!results.Any()) return "No relevant content found.";

                var bestResult = results.First();
                var content = bestResult.Content ?? "";

                if (!string.IsNullOrEmpty(content))
                {
                    return await GetGenerativeAnswers(query, bestResult.FilePath ?? "", content);
                }

                return $"Found relevant content in {Path.GetFileName(bestResult.FilePath)} but couldn't extract detailed information.";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating AI answer: {ex.Message}");
                return "Unable to generate AI answer from the refined results.";
            }
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
    }
}