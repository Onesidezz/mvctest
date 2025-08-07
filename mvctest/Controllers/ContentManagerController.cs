using DocumentFormat.OpenXml.Drawing;
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

        public IActionResult SearchResults(string content)
        {
            try
            {
                Console.WriteLine($"SearchResults called with query: '{content}'");
                
                // Initialize with empty list to prevent null reference
                var searchResults = new List<Models.SearchResultModel>();
                
                if (string.IsNullOrWhiteSpace(content))
                {
                    TempData["InfoMessage"] = "Please enter a search query.";
                    return View(searchResults);
                }
                
                // Check index status first
                _luceneInterface.ShowIndexStats();
                
                // Call SearchFiles and handle null return
                var luceneResults = _luceneInterface.SearchFiles(content);
                
                Console.WriteLine($"SearchFiles returned {luceneResults?.Count ?? 0} results");
                
                if (luceneResults != null && luceneResults.Any())
                {
                    searchResults = luceneResults;
                    TempData["SuccessMessage"] = $"Found {searchResults.Count} search results.";
                }
                else
                {
                    Console.WriteLine("No search results found - checking if index is empty");
                    TempData["InfoMessage"] = "No search results found. The index might be empty or the query didn't match any documents. Make sure files are indexed in the API project first.";
                    
                    // Try semantic search as fallback
                    try
                    {
                        Console.WriteLine("Trying semantic search as fallback...");
                        var semanticResults = _luceneInterface.SemanticSearch(content, maxResults: 10);
                        if (semanticResults != null && semanticResults.Any())
                        {
                            searchResults = semanticResults;
                            TempData["SuccessMessage"] = $"Found {searchResults.Count} semantic search results.";
                            Console.WriteLine($"Semantic search found {searchResults.Count} results");
                        }
                    }
                    catch (Exception semanticEx)
                    {
                        Console.WriteLine($"Semantic search also failed: {semanticEx.Message}");
                    }
                }
                
                return View(searchResults); // Always pass a non-null list
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SearchResults error: {ex.Message}");
                Console.WriteLine($"SearchResults stack trace: {ex.StackTrace}");
                TempData["ErrorMessage"] = $"Search failed: {ex.Message}";
                
                // Return empty results instead of redirecting to prevent view errors
                var emptyResults = new List<Models.SearchResultModel>();
                return View(emptyResults);
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
                return BadRequest(new { error = $"Failed to generate summary: {ex.Message}" });
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

                Console.WriteLine($"DeepContentSearch: Advanced file-by-file analysis for query: '{request.Query}'");
                Console.WriteLine($"Analyzing {request.FilePaths.Count} files for relevant content");
                
                var relevantFiles = new List<(string filePath, float relevanceScore, string relevantContent)>();
                var searchTypes = new List<string> { "Advanced File Analysis with Generative AI" };
                
                // Step 1: Loop through each file to find relevant content
                foreach (var filePath in request.FilePaths)
                {
                    try
                    {
                        if (!System.IO.File.Exists(filePath))
                        {
                            Console.WriteLine($"File not found: {filePath}");
                            continue;
                        }

                        Console.WriteLine($"Analyzing file: {System.IO.Path.GetFileName(filePath)}");
                        
                        // Extract content from file
                        var content = Services.FileTextExtractor.ExtractTextFromFile(filePath);
                        if (string.IsNullOrEmpty(content))
                        {
                            Console.WriteLine($"No content extracted from: {System.IO.Path.GetFileName(filePath)}");
                            continue;
                        }

                        // Check relevance using semantic similarity
                        var relevanceScore = await CalculateContentRelevance(request.Query, content);
                        
                        if (relevanceScore > 0.1f) // Only include files with some relevance
                        {
                            relevantFiles.Add((filePath, relevanceScore, content));
                            Console.WriteLine($"✓ Found relevant content in {System.IO.Path.GetFileName(filePath)} (relevance: {relevanceScore:F3})");
                        }
                        else
                        {
                            Console.WriteLine($"✗ No relevant content found in {System.IO.Path.GetFileName(filePath)} (relevance: {relevanceScore:F3})");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error analyzing file {filePath}: {ex.Message}");
                    }
                }

                // Step 2: Sort by relevance and process most relevant files
                relevantFiles = relevantFiles.OrderByDescending(f => f.relevanceScore).ToList();
                
                string enhancedAnswer;
                if (relevantFiles.Any())
                {
                    Console.WriteLine($"Found {relevantFiles.Count} relevant files, processing top matches for generative answer");
                    
                    // Step 3: Use the most relevant files for generative AI analysis
                    var topRelevantFiles = relevantFiles.Take(3).ToList(); // Top 3 most relevant files
                    
                    var generativeAnswers = new List<string>();
                    
                    foreach (var (filePath, score, relevantContent) in topRelevantFiles)
                    {
                        try
                        {
                            Console.WriteLine($"Getting generative answer from {System.IO.Path.GetFileName(filePath)} (relevance: {score:F3})");
                            
                            // Use the new GetGenerativeAnswers function
                            var fileAnswer = await GetGenerativeAnswers(request.Query, filePath, relevantContent);
                            
                            if (!string.IsNullOrEmpty(fileAnswer))
                            {
                                generativeAnswers.Add($"From {System.IO.Path.GetFileName(filePath)}: {fileAnswer}");
                                Console.WriteLine($"✓ Generated answer from {System.IO.Path.GetFileName(filePath)}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error getting generative answer from {filePath}: {ex.Message}");
                        }
                    }
                    
                    // Step 4: Combine all generative answers into final response
                    if (generativeAnswers.Any())
                    {
                        // Create comprehensive answer by combining insights from multiple files
                        var combinedContext = string.Join("\n\n", generativeAnswers);
                        enhancedAnswer = await _chatMLService.GetChatBotResponse(
                            $"Based on the following information from multiple documents, provide a comprehensive answer to: '{request.Query}'\n\n{combinedContext}",
                            isFromDeepseek: true
                        );
                        
                        if (string.IsNullOrEmpty(enhancedAnswer))
                        {
                            // Fallback: Use the best individual answer
                            enhancedAnswer = generativeAnswers.First();
                        }
                    }
                    else
                    {
                        // Fallback: Use most relevant content directly
                        var bestFile = topRelevantFiles.First();
                        var snippet = bestFile.relevantContent.Length > 500 ? bestFile.relevantContent.Substring(0, 500) + "..." : bestFile.relevantContent;
                        enhancedAnswer = $"Based on content from {System.IO.Path.GetFileName(bestFile.filePath)}: {snippet}";
                    }
                }
                else
                {
                    Console.WriteLine("No relevant content found in any files");
                    enhancedAnswer = $"I couldn't find any information related to '{request.Query}' in the provided documents. The content may not be relevant to your question, or the files may not contain the information you're looking for.";
                }

                return Json(new { 
                    answer = enhancedAnswer,
                    searchMetadata = new {
                        searchTypes = searchTypes,
                        resultsFound = relevantFiles.Count,
                        highResolutionEnabled = true,
                        queryType = "Advanced File-by-File Generative Analysis",
                        filesSearched = request.FilePaths.Count,
                        filesWithMatches = relevantFiles.Count,
                        averageSimilarity = relevantFiles.Any() ? relevantFiles.Average(f => f.relevanceScore) : 0f,
                        topSimilarity = relevantFiles.Any() ? relevantFiles.Max(f => f.relevanceScore) : 0f,
                        relevantFiles = relevantFiles.Take(5).Select(f => new { 
                            fileName = System.IO.Path.GetFileName(f.filePath),
                            relevanceScore = f.relevanceScore 
                        }).ToList()
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DeepContentSearch error: {ex.Message}");
                return BadRequest(new { error = $"Failed to perform advanced content search: {ex.Message}" });
            }
        }

        private async Task ProcessDocumentWithContentBlocks(string filePath, string content, string query, float[] queryEmbedding, TextEmbeddingService embeddingService, List<(string filePath, float similarity, List<string> relevantChunks, string documentType)> semanticMatches)
        {
            // 1. Process main document (like IndexMainDocument in OlamaApi)
            await ProcessMainDocument(filePath, content, queryEmbedding, embeddingService, semanticMatches);
            
            // 2. Process content blocks (like IndexContentBlocks in OlamaApi)  
            await ProcessContentBlocks(filePath, content, queryEmbedding, embeddingService, semanticMatches);
        }

        private async Task ProcessMainDocument(string filePath, string content, float[] queryEmbedding, TextEmbeddingService embeddingService, List<(string filePath, float similarity, List<string> relevantChunks, string documentType)> semanticMatches)
        {
            try
            {
                // Generate embedding for entire document content
                var documentEmbedding = embeddingService?.GetEmbedding(content);
                if (documentEmbedding != null && queryEmbedding != null)
                {
                    var similarity = TextEmbeddingService.CosineSimilarity(queryEmbedding, documentEmbedding);
                    
                    // Consider main document relevant if similarity above threshold
                    if (similarity > 0.25f) // Lower threshold for main documents
                    {
                        var relevantChunks = new List<string> { content.Substring(0, Math.Min(1000, content.Length)) }; // First 1000 chars
                        semanticMatches.Add((filePath, similarity, relevantChunks, "main"));
                        Console.WriteLine($"✓ Found main document match in {System.IO.Path.GetFileName(filePath)} (similarity: {similarity:F3})");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing main document {filePath}: {ex.Message}");
            }
        }

        private async Task ProcessContentBlocks(string filePath, string content, float[] queryEmbedding, TextEmbeddingService embeddingService, List<(string filePath, float similarity, List<string> relevantChunks, string documentType)> semanticMatches)
        {
            try
            {
                // Split into paragraphs (like IdentifyContentBlocks in OlamaApi)
                var paragraphs = content.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                
                foreach (var paragraph in paragraphs)
                {
                    if (!string.IsNullOrWhiteSpace(paragraph) && paragraph.Trim().Length > 50)
                    {
                        var paragraphEmbedding = embeddingService?.GetEmbedding(paragraph.Trim());
                        if (paragraphEmbedding != null && queryEmbedding != null)
                        {
                            var similarity = TextEmbeddingService.CosineSimilarity(queryEmbedding, paragraphEmbedding);
                            
                            if (similarity > 0.3f) // Higher threshold for content blocks
                            {
                                var relevantChunks = new List<string> { paragraph.Trim() };
                                semanticMatches.Add((filePath, similarity, relevantChunks, "content_block"));
                                Console.WriteLine($"✓ Found content block match in {System.IO.Path.GetFileName(filePath)} (similarity: {similarity:F3})");
                            }
                        }
                    }
                }

                // Split into sentences as well
                var sentences = content.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(s => !string.IsNullOrWhiteSpace(s) && s.Trim().Length > 20)
                    .ToArray();
                
                foreach (var sentence in sentences)
                {
                    var sentenceEmbedding = embeddingService?.GetEmbedding(sentence.Trim());
                    if (sentenceEmbedding != null && queryEmbedding != null)
                    {
                        var similarity = TextEmbeddingService.CosineSimilarity(queryEmbedding, sentenceEmbedding);
                        
                        if (similarity > 0.35f) // Even higher threshold for sentences
                        {
                            var relevantChunks = new List<string> { sentence.Trim() };
                            semanticMatches.Add((filePath, similarity, relevantChunks, "sentence"));
                            Console.WriteLine($"✓ Found sentence match in {System.IO.Path.GetFileName(filePath)} (similarity: {similarity:F3})");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing content blocks for {filePath}: {ex.Message}");
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
    }
}
