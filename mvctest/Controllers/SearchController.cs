using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Options;
using mvctest.Models;
using mvctest.Services;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace mvctest.Controllers
{
    public class SearchController : Controller
    {
        private readonly IContentManager _contentManager;
        private readonly ISessionSearchManager _sessionSearchManager;
        private readonly IChatMLService _chatMLService;
        private readonly ContentManagerHelperService _helperService;
        private readonly ILuceneInterface _luceneService;

        public SearchController(IContentManager contentManager, ISessionSearchManager sessionSearchManager, IChatMLService chatMLService, ContentManagerHelperService helperService, ILuceneInterface luceneService)
        {
            _contentManager = contentManager;
            _sessionSearchManager = sessionSearchManager;
            _chatMLService = chatMLService;
            _helperService = helperService;
            _luceneService = luceneService;
        }
        public IActionResult Index()
        {
            return View();
        }
        [HttpPost]
        public async Task<IActionResult> Result([FromBody] List<Dictionary<string, string>> search, int page = 1, int pageSize = 10)
        {
            try
            {
                if (search == null || !search.Any())
                {
                    TempData["ErrorMessage"] = "No search filters provided";
                    return RedirectToAction("Index");
                }

                // Create search session using the service
                var searchId = await _sessionSearchManager.CreateSearchSessionAsync(search, HttpContext);

                // Execute search using ContentManager  
                var searchResults = await Task.Run(() => _contentManager.GetRecordsWithPaganited(search, page, pageSize));
                
                return View("Results", searchResults);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Search failed: {ex.Message}";
                return RedirectToAction("Index");
            }
        }

        [HttpGet]
        public async Task<IActionResult> Result(int page = 1, int pageSize = 10)
        {
            try
            {
                // Get current search ID from session
                var searchId = HttpContext.Session.GetString("CurrentSearchId");
                
                if (string.IsNullOrEmpty(searchId))
                {
                    TempData["ErrorMessage"] = "Search session expired. Please perform a new search.";
                    return RedirectToAction("Index");
                }

                // Get stored search filters using the service
                var storedSearch = await _sessionSearchManager.GetSearchFiltersAsync(searchId, HttpContext);
                
                if (storedSearch == null || !storedSearch.Any())
                {
                    TempData["ErrorMessage"] = "Search session expired. Please perform a new search.";
                    return RedirectToAction("Index");
                }

                // Execute search using ContentManager - Only get the page we need
                var searchResults = await Task.Run(() => _contentManager.GetRecordsWithPaganited(storedSearch, page, pageSize));

                // Store search filters in ViewBag for display
                ViewBag.SearchFiltersJson = System.Text.Json.JsonSerializer.Serialize(storedSearch);
                
                return View("_Results", searchResults);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Search failed: {ex.Message}";
                return RedirectToAction("Index");
            }
        }

        // AJAX endpoint for getting search results as partial view
        [HttpPost]
        public async Task<IActionResult> GetResults([FromBody] List<Dictionary<string, string>> search, int page = 1, int pageSize = 10)
        {
            try
            {
                if (search == null || !search.Any())
                {
                    return Json(new { success = false, message = "No search filters provided" });
                }

                // Check if this is a ContentSearch request
                var contentSearchFilter = search.FirstOrDefault(s => s.ContainsKey("ContentSearch"));
                var isContentSearch = contentSearchFilter != null;

                if (isContentSearch)
                {
                    // Handle index search
                    var searchResults = await PerformIndexSearch(contentSearchFilter["ContentSearch"], page, pageSize);
                    
                    // Store search filters in ViewBag for display
                    ViewBag.SearchFiltersJson = System.Text.Json.JsonSerializer.Serialize(search);
                    
                    // Render partial view to string
                    var partialViewResult = await RenderPartialViewToStringAsync("_Results", searchResults);
                    
                    return Json(new { 
                        success = true, 
                        html = partialViewResult,
                        totalRecords = searchResults.TotalRecords,
                        currentPage = page,
                        pageSize = pageSize
                    });
                }
                else
                {
                    // Create search session using the service
                    var searchId = await _sessionSearchManager.CreateSearchSessionAsync(search, HttpContext);

                    // Execute regular search using ContentManager  
                    var searchResults = await Task.Run(() => _contentManager.GetRecordsWithPaganited(search, page, pageSize));

                    // Store search filters in ViewBag for display
                    ViewBag.SearchFiltersJson = System.Text.Json.JsonSerializer.Serialize(search);
                    
                    // Render partial view to string
                    var partialViewResult = await RenderPartialViewToStringAsync("_Results", searchResults);
                    
                    return Json(new { 
                        success = true, 
                        html = partialViewResult,
                        totalRecords = searchResults.TotalRecords,
                        currentPage = page,
                        pageSize = pageSize
                    });
                }
            }
            catch (Exception ex)
            {
                return Json(new { 
                    success = false, 
                    message = $"Search failed: {ex.Message}" 
                });
            }
        }

        // Helper method to render partial view to string
        private async Task<string> RenderPartialViewToStringAsync(string viewName, object model)
        {
            ViewData.Model = model;
            
            using var writer = new StringWriter();
            var viewEngine = HttpContext.RequestServices.GetService(typeof(ICompositeViewEngine)) as ICompositeViewEngine;
            var viewResult = viewEngine.FindView(ControllerContext, viewName, false);
            
            if (!viewResult.Success)
            {
                throw new ArgumentNullException($"Partial view '{viewName}' not found");
            }
            
            var viewContext = new ViewContext(ControllerContext, viewResult.View, ViewData, TempData, writer, new HtmlHelperOptions());
            await viewResult.View.RenderAsync(viewContext);
            
            return writer.GetStringBuilder().ToString();
        }

        // Index search method matching SyncApplication indexing structure
        private async Task<PaginatedRecordViewModel> PerformIndexSearch(string query, int page = 1, int pageSize = 10)
        {
            try
            {
                // Perform dedicated search for SyncedRecords from IndexDirectory2 (matching SyncApplication indexing)
                var searchResults = await Task.Run(() => _luceneService.SearchSyncedRecords(query));
                
                // Calculate pagination
                var totalRecords = searchResults.Count;
                var skip = (page - 1) * pageSize;
                var paginatedResults = searchResults.Skip(skip).Take(pageSize).ToList();

                // Convert to RecordViewModel objects with complete metadata
                var records = paginatedResults.Select(result => new RecordViewModel
                {
                    // Primary fields from indexed record
                    URI = TryParseLong(result.Metadata?.ContainsKey("URI") == true ? result.Metadata["URI"] : null),
                    Title = result.Metadata?.ContainsKey("Title") == true ? result.Metadata["Title"] : result.FileName,
                    Container = result.Metadata?.ContainsKey("Container") == true ? result.Metadata["Container"] : "Lucene Index Results",
                    
                    // Geographic and organizational fields
                    Region = result.Metadata?.ContainsKey("Region") == true ? result.Metadata["Region"] : "",
                    Country = result.Metadata?.ContainsKey("Country") == true ? result.Metadata["Country"] : "",
                    ClientId = result.Metadata?.ContainsKey("ClientId") == true ? result.Metadata["ClientId"] : "",
                    
                    // Business fields
                    BillTo = result.Metadata?.ContainsKey("BillTo") == true ? result.Metadata["BillTo"] : "",
                    ShipTo = result.Metadata?.ContainsKey("ShipTo") == true ? result.Metadata["ShipTo"] : "",
                    
                    // Additional metadata fields
                    Assignee = result.Metadata?.ContainsKey("Assignee") == true ? result.Metadata["Assignee"] : "",
                    DateCreated = result.Metadata?.ContainsKey("DateCreated") == true ? result.Metadata["DateCreated"] : result.date ?? "",
                    IsContainer = result.Metadata?.ContainsKey("IsContainer") == true ? result.Metadata["IsContainer"] : "",
                    
                    // File and download information
                    DownloadLink = result.Metadata?.ContainsKey("DownloadLink") == true ? result.Metadata["DownloadLink"] : result.FilePath,
                    
                    // Content preview - show search snippets with context
                    AllParts = BuildContentPreview(result, query)
                    
                }).ToList();

                // Create PaginatedRecordViewModel
                return new PaginatedRecordViewModel
                {
                    Records = records,
                    TotalRecords = totalRecords,
                    CurrentPage = page,
                    PageSize = pageSize
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error performing index search: {ex.Message}");
                
                // Return empty result set on error
                return new PaginatedRecordViewModel
                {
                    Records = new List<RecordViewModel>(),
                    TotalRecords = 0,
                    CurrentPage = page,
                    PageSize = pageSize
                };
            }
        }

        // Helper method to safely parse long values
        private long? TryParseLong(string value)
        {
            if (string.IsNullOrEmpty(value))
                return null;
                
            if (long.TryParse(value, out long result))
                return result;
                
            return null;
        }

        // Helper method to build content preview from search results
        private string BuildContentPreview(SearchResultModel result, string query)
        {
            var preview = new List<string>();
            
            // Add search snippets if available
            if (result.Snippets != null && result.Snippets.Any())
            {
                preview.AddRange(result.Snippets.Take(3)); // Show up to 3 snippets
            }
            
            // Add file content preview if no snippets but content exists
            if (!preview.Any() && result.Metadata?.ContainsKey("file_content") == true)
            {
                var content = result.Metadata["file_content"];
                if (!string.IsNullOrEmpty(content))
                {
                    // Extract relevant portion containing query terms
                    var queryWords = query.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var contentLower = content.ToLower();
                    
                    foreach (var word in queryWords)
                    {
                        var index = contentLower.IndexOf(word);
                        if (index >= 0)
                        {
                            var start = Math.Max(0, index - 50);
                            var length = Math.Min(200, content.Length - start);
                            var snippet = content.Substring(start, length);
                            
                            if (start > 0) snippet = "..." + snippet;
                            if (start + length < content.Length) snippet = snippet + "...";
                            
                            preview.Add(snippet);
                            break; // Just show one content snippet
                        }
                    }
                }
            }
            
            // Fallback to filename if no content preview available
            if (!preview.Any())
            {
                preview.Add($"Search match found in: {result.FileName}");
            }
            
            return string.Join(" | ", preview);
        }

        // Helper method to extract URI from file path (fallback)
        private long? GetUriFromFilePath(string filePath)
        {
            // This is a placeholder - implement according to your URI structure
            if (string.IsNullOrEmpty(filePath))
                return null;
            
            // Generate a hash-based URI from file path for consistent identification
            return Math.Abs(filePath.GetHashCode());
        }

        // Export selected records to CSV
        [HttpPost]
        public IActionResult ExportToCSV([FromBody] ExportRequest request)
        {
            try
            {
                Console.WriteLine($"Export request received. Request is null: {request == null}");
                if (request != null)
                {
                    Console.WriteLine($"Records is null: {request.Records == null}");
                    Console.WriteLine($"Records count: {request.Records?.Count ?? 0}");
                }

                if (request?.Records == null || !request.Records.Any())
                {
                    return Json(new { success = false, message = $"No records to export. Request: {request != null}, Records: {request?.Records?.Count ?? 0}" });
                }

                // Ensure directory exists
                var directory = Path.GetDirectoryName(request.FilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Generate CSV content
                var csvBuilder = new StringBuilder();
                csvBuilder.AppendLine("Client ID,Title,Container,Country,Region,Bill To,Ship To");

                foreach (var record in request.Records)
                {
                    var line = $"\"{record.ClientId}\",\"{record.Title}\",\"{record.Container}\",\"{record.Country}\",\"{record.Region}\",\"{record.BillTo}\",\"{record.ShipTo}\"";
                    csvBuilder.AppendLine(line);
                }

                // Write to file
                System.IO.File.WriteAllText(request.FilePath, csvBuilder.ToString());

                return Json(new { success = true, message = $"CSV exported successfully to {request.FilePath}" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }


        // Download selected files
        [HttpPost]
        public IActionResult DownloadSelected(List<long> selectedIds)
        {
            try
            {
                if (selectedIds == null || !selectedIds.Any())
                {
                    return Json(new { success = false, message = "No files selected for download" });
                }

                Console.WriteLine($"Download request for {selectedIds.Count} files: {string.Join(", ", selectedIds)}");

                // If only one file selected, return it directly with original extension
                if (selectedIds.Count == 1)
                {
                    try
                    {
                        var fileHandler = _contentManager.Download((int)selectedIds[0]);
                        if (fileHandler != null && fileHandler.File != null)
                        {
                            // Determine content type based on file extension
                            var contentType = GetContentType(fileHandler.FileName);
                            
                            Console.WriteLine($"Single file download: {fileHandler.FileName} ({fileHandler.File.Length} bytes)");
                            
                            return File(fileHandler.File, contentType, fileHandler.FileName);
                        }
                        else
                        {
                            return Json(new { success = false, message = "Selected file could not be found or accessed" });
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error downloading single file with ID {selectedIds[0]}: {ex.Message}");
                        return Json(new { success = false, message = $"Error downloading file: {ex.Message}" });
                    }
                }

                // Multiple files - create ZIP archive
                using (var memoryStream = new MemoryStream())
                {
                    using (var archive = new System.IO.Compression.ZipArchive(memoryStream, System.IO.Compression.ZipArchiveMode.Create, true))
                    {
                        var successCount = 0;
                        foreach (var id in selectedIds)
                        {
                            try
                            {
                                var fileHandler = _contentManager.Download((int)id);
                                if (fileHandler != null && fileHandler.File != null)
                                {
                                    var zipEntry = archive.CreateEntry(fileHandler.FileName);
                                    using (var entryStream = zipEntry.Open())
                                    using (var fileStream = new MemoryStream(fileHandler.File))
                                    {
                                        fileStream.CopyTo(entryStream);
                                        successCount++;
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error downloading file with ID {id}: {ex.Message}");
                            }
                        }
                    }

                    if (memoryStream.Length == 0)
                    {
                        return Json(new { success = false, message = "No files could be added to the ZIP archive" });
                    }

                    // Return the ZIP file for multiple files
                    var zipFileName = "SelectedFiles.zip";
                    
                    Console.WriteLine($"ZIP file created successfully with {memoryStream.Length} bytes");
                    
                    return File(memoryStream.ToArray(), "application/zip", zipFileName);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Download failed: {ex.Message}");
                return Json(new { success = false, message = $"Download failed: {ex.Message}" });
            }
        }

        // Helper method to get content type based on file extension
        private string GetContentType(string fileName)
        {
            var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
            
            return extension switch
            {
                ".pdf" => "application/pdf",
                ".doc" => "application/msword",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                ".xls" => "application/vnd.ms-excel",
                ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".ppt" => "application/vnd.ms-powerpoint",
                ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
                ".txt" => "text/plain",
                ".csv" => "text/csv",
                ".json" => "application/json",
                ".xml" => "application/xml",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".tiff" or ".tif" => "image/tiff",
                ".zip" => "application/zip",
                ".rar" => "application/x-rar-compressed",
                ".7z" => "application/x-7z-compressed",
                _ => "application/octet-stream"
            };
        }

        // View single file in browser with mixed strategy
        [HttpGet]
        public IActionResult ViewFile(long id)
        {
            try
            {
                var fileHandler = _contentManager.Download((int)id);
                if (fileHandler != null && fileHandler.File != null)
                {
                    var extension = Path.GetExtension(fileHandler.FileName)?.ToLowerInvariant();
                    
                    Console.WriteLine($"Viewing file: {fileHandler.FileName} ({fileHandler.File.Length} bytes)");
                    
                    // Mixed strategy based on file type
                    return extension switch
                    {
                        // Direct browser view (native support)
                        ".pdf" or ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".tiff" or ".tif" 
                            => ViewFileDirectly(fileHandler),
                        
                        // Convert to HTML table
                        ".csv" => ViewCsvAsHtml(fileHandler),
                        
                        // Convert Excel to HTML table
                        ".xlsx" or ".xls" => ViewExcelAsHtml(fileHandler),
                        
                        // Convert Word to HTML
                        ".docx" or ".doc" => ViewWordAsHtml(fileHandler),
                        
                        // PowerPoint files - direct download
                        ".pptx" or ".ppt" => DownloadFile(fileHandler),
                        
                        // Text files can be viewed directly
                        ".txt" or ".json" or ".xml" => ViewTextFile(fileHandler),
                        
                        // Default: download
                        _ => DownloadFile(fileHandler)
                    };
                }
                else
                {
                    return NotFound("File not found or could not be accessed.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error viewing file with ID {id}: {ex.Message}");
                return BadRequest($"Error viewing file: {ex.Message}");
            }
        }

        // Direct file view for PDFs, images
        private IActionResult ViewFileDirectly(dynamic fileHandler)
        {
            var contentType = GetContentType(fileHandler.FileName);
            Response.Headers.Add("Content-Disposition", $"inline; filename=\"{fileHandler.FileName}\"");
            return File(fileHandler.File, contentType);
        }

        // Download file
        private IActionResult DownloadFile(dynamic fileHandler)
        {
            var contentType = GetContentType(fileHandler.FileName);
            Response.Headers.Add("Content-Disposition", $"attachment; filename=\"{fileHandler.FileName}\"");
            return File(fileHandler.File, contentType);
        }

        // View text files
        private IActionResult ViewTextFile(dynamic fileHandler)
        {
            var content = System.Text.Encoding.UTF8.GetString(fileHandler.File);
            var extension = Path.GetExtension(fileHandler.FileName)?.ToLowerInvariant();
            
            var html = $@"
<!DOCTYPE html>
<html>
<head>
    <title>{fileHandler.FileName}</title>
    <meta charset='utf-8'>
    <style>
        body {{ font-family: 'Courier New', monospace; margin: 20px; background: #f8f9fa; }}
        .container {{ max-width: 1200px; margin: 0 auto; background: white; padding: 20px; border-radius: 8px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }}
        .header {{ border-bottom: 2px solid #007bff; padding-bottom: 10px; margin-bottom: 20px; }}
        .filename {{ color: #007bff; font-size: 18px; font-weight: bold; }}
        .content {{ white-space: pre-wrap; background: #f8f9fa; padding: 15px; border-radius: 5px; border: 1px solid #dee2e6; }}
        {(extension == ".json" ? ".content { color: #d14; }" : "")}
        {(extension == ".xml" ? ".content { color: #0066cc; }" : "")}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <div class='filename'>📄 {fileHandler.FileName}</div>
        </div>
        <div class='content'>{System.Web.HttpUtility.HtmlEncode(content)}</div>
    </div>
</body>
</html>";
            
            return Content(html, "text/html");
        }

        // View CSV as HTML table
        private IActionResult ViewCsvAsHtml(dynamic fileHandler)
        {
            try
            {
                var csvContent = System.Text.Encoding.UTF8.GetString(fileHandler.File);
                var lines = csvContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                
                if (lines.Length == 0)
                {
                    return Content("Empty CSV file", "text/plain");
                }

                var html = new StringBuilder();
                html.Append($@"
<!DOCTYPE html>
<html>
<head>
    <title>{fileHandler.FileName}</title>
    <meta charset='utf-8'>
    <link href='https://cdn.jsdelivr.net/npm/bootstrap@5.1.3/dist/css/bootstrap.min.css' rel='stylesheet'>
    <style>
        body {{ background: #f8f9fa; padding: 20px; }}
        .container {{ background: white; padding: 20px; border-radius: 8px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }}
        .table {{ font-size: 14px; }}
        .header {{ border-bottom: 2px solid #007bff; padding-bottom: 10px; margin-bottom: 20px; }}
        .filename {{ color: #007bff; font-size: 18px; font-weight: bold; }}
        th {{ background: #007bff !important; color: white !important; position: sticky; top: 0; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <div class='filename'>📊 {fileHandler.FileName}</div>
            <small class='text-muted'>{lines.Length} rows</small>
        </div>
        <div class='table-responsive'>
            <table class='table table-striped table-hover'>
");

                for (int i = 0; i < lines.Length; i++)
                {
                    var cells = ParseCsvLine(lines[i]);
                    
                    if (i == 0)
                    {
                        // Header row
                        html.Append("<thead><tr>");
                        foreach (var cell in cells)
                        {
                            html.Append($"<th>{System.Web.HttpUtility.HtmlEncode(cell)}</th>");
                        }
                        html.Append("</tr></thead><tbody>");
                    }
                    else
                    {
                        // Data rows
                        html.Append("<tr>");
                        foreach (var cell in cells)
                        {
                            html.Append($"<td>{System.Web.HttpUtility.HtmlEncode(cell)}</td>");
                        }
                        html.Append("</tr>");
                    }
                }

                html.Append(@"
            </tbody>
        </table>
        </div>
    </div>
</body>
</html>");

                return Content(html.ToString(), "text/html");
            }
            catch (Exception ex)
            {
                return Content($"Error parsing CSV: {ex.Message}", "text/plain");
            }
        }

        // Simple CSV parser
        private List<string> ParseCsvLine(string line)
        {
            var cells = new List<string>();
            var currentCell = new StringBuilder();
            bool inQuotes = false;
            
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                
                if (c == '"' && (i == 0 || line[i-1] != '\\'))
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    cells.Add(currentCell.ToString().Trim());
                    currentCell.Clear();
                }
                else
                {
                    currentCell.Append(c);
                }
            }
            
            cells.Add(currentCell.ToString().Trim());
            return cells;
        }

        // View Excel as HTML using EPPlus
        private IActionResult ViewExcelAsHtml(dynamic fileHandler)
        {
            try
            {
                // Set EPPlus license context for non-commercial use
                OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;
                
                using (var stream = new MemoryStream(fileHandler.File))
                {
                    using (var package = new OfficeOpenXml.ExcelPackage(stream))
                    {
                        var html = new StringBuilder();
                        html.Append($@"
<!DOCTYPE html>
<html>
<head>
    <title>{fileHandler.FileName}</title>
    <meta charset='utf-8'>
    <link href='https://cdn.jsdelivr.net/npm/bootstrap@5.1.3/dist/css/bootstrap.min.css' rel='stylesheet'>
    <style>
        body {{ background: #f8f9fa; padding: 20px; }}
        .container {{ background: white; padding: 20px; border-radius: 8px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }}
        .sheet-tabs {{ margin-bottom: 20px; }}
        .sheet-content {{ display: none; }}
        .sheet-content.active {{ display: block; }}
        .table {{ font-size: 12px; }}
        .header {{ border-bottom: 2px solid #007bff; padding-bottom: 10px; margin-bottom: 20px; }}
        .filename {{ color: #007bff; font-size: 18px; font-weight: bold; }}
        th {{ background: #007bff !important; color: white !important; position: sticky; top: 0; }}
        .cell-number {{ text-align: right; }}
        .empty-cell {{ color: #6c757d; font-style: italic; }}
    </style>
    <script>
        function showSheet(sheetName) {{
            document.querySelectorAll('.sheet-content').forEach(el => el.classList.remove('active'));
            document.querySelectorAll('.nav-link').forEach(el => el.classList.remove('active'));
            document.getElementById('sheet-' + sheetName).classList.add('active');
            document.getElementById('tab-' + sheetName).classList.add('active');
        }}
    </script>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <div class='filename'>📈 {fileHandler.FileName}</div>
            <small class='text-muted'>{package.Workbook.Worksheets.Count} worksheet(s)</small>
        </div>");

                        // Create tabs for multiple sheets
                        if (package.Workbook.Worksheets.Count > 1)
                        {
                            html.Append("<ul class='nav nav-tabs sheet-tabs'>");
                            for (int i = 0; i < package.Workbook.Worksheets.Count; i++)
                            {
                                var worksheet = package.Workbook.Worksheets[i];
                                var isActive = i == 0 ? "active" : "";
                                var sheetId = worksheet.Name.Replace(" ", "_").Replace("'", "");
                                html.Append($"<li class='nav-item'><a class='nav-link {isActive}' id='tab-{sheetId}' href='#' onclick='showSheet(\"{sheetId}\")'>{worksheet.Name}</a></li>");
                            }
                            html.Append("</ul>");
                        }

                        // Process each worksheet
                        for (int sheetIndex = 0; sheetIndex < package.Workbook.Worksheets.Count; sheetIndex++)
                        {
                            var worksheet = package.Workbook.Worksheets[sheetIndex];
                            var sheetId = worksheet.Name.Replace(" ", "_").Replace("'", "");
                            var isActive = sheetIndex == 0 ? "active" : "";
                            
                            html.Append($"<div id='sheet-{sheetId}' class='sheet-content {isActive}'>");
                            
                            if (package.Workbook.Worksheets.Count > 1)
                            {
                                html.Append($"<h5>📊 {worksheet.Name}</h5>");
                            }

                            // Get the used range
                            var start = worksheet.Dimension?.Start;
                            var end = worksheet.Dimension?.End;
                            
                            if (start != null && end != null)
                            {
                                html.Append($"<p class='text-muted'>Range: {worksheet.Dimension.Address} ({end.Row - start.Row + 1} rows, {end.Column - start.Column + 1} columns)</p>");
                                html.Append("<div class='table-responsive'><table class='table table-striped table-hover table-bordered'>");

                                // Generate table
                                for (int row = start.Row; row <= Math.Min(end.Row, start.Row + 999); row++) // Limit to 1000 rows
                                {
                                    if (row == start.Row)
                                    {
                                        html.Append("<thead><tr><th>#</th>");
                                        for (int col = start.Column; col <= Math.Min(end.Column, start.Column + 49); col++) // Limit to 50 columns
                                        {
                                            var cellValue = worksheet.Cells[row, col].Value?.ToString() ?? "";
                                            if (string.IsNullOrWhiteSpace(cellValue))
                                                cellValue = GetColumnName(col);
                                            html.Append($"<th>{System.Web.HttpUtility.HtmlEncode(cellValue)}</th>");
                                        }
                                        html.Append("</tr></thead><tbody>");
                                    }
                                    else
                                    {
                                        html.Append($"<tr><td class='cell-number'>{row}</td>");
                                        for (int col = start.Column; col <= Math.Min(end.Column, start.Column + 49); col++)
                                        {
                                            var cellValue = worksheet.Cells[row, col].Value?.ToString() ?? "";
                                            if (string.IsNullOrWhiteSpace(cellValue))
                                            {
                                                html.Append("<td class='empty-cell'>-</td>");
                                            }
                                            else
                                            {
                                                // Check if it's a number for right alignment
                                                if (double.TryParse(cellValue, out _))
                                                {
                                                    html.Append($"<td class='cell-number'>{System.Web.HttpUtility.HtmlEncode(cellValue)}</td>");
                                                }
                                                else
                                                {
                                                    html.Append($"<td>{System.Web.HttpUtility.HtmlEncode(cellValue)}</td>");
                                                }
                                            }
                                        }
                                        html.Append("</tr>");
                                    }
                                }

                                html.Append("</tbody></table></div>");
                                
                                if (end.Row > start.Row + 999)
                                {
                                    html.Append($"<div class='alert alert-info'>Note: Only showing first 1000 rows. Total rows: {end.Row - start.Row + 1}</div>");
                                }
                            }
                            else
                            {
                                html.Append("<div class='alert alert-warning'>This worksheet appears to be empty.</div>");
                            }
                            
                            html.Append("</div>");
                        }

                        html.Append("</div></body></html>");
                        return Content(html.ToString(), "text/html");
                    }
                }
            }
            catch (Exception ex)
            {
                return Content($@"
<!DOCTYPE html>
<html>
<head>
    <title>{fileHandler.FileName}</title>
    <meta charset='utf-8'>
    <link href='https://cdn.jsdelivr.net/npm/bootstrap@5.1.3/dist/css/bootstrap.min.css' rel='stylesheet'>
</head>
<body>
    <div class='container mt-4'>
        <div class='alert alert-danger'>
            <h4>📈 Excel File: {fileHandler.FileName}</h4>
            <p>Error reading Excel file: {ex.Message}</p>
            <p><a href='#' onclick='window.close()'>Close this tab</a> and try downloading the file instead.</p>
        </div>
    </div>
</body>
</html>", "text/html");
            }
        }

        // Helper method to get Excel column name (A, B, C, etc.)
        private string GetColumnName(int columnNumber)
        {
            string columnName = "";
            while (columnNumber > 0)
            {
                int modulo = (columnNumber - 1) % 26;
                columnName = Convert.ToChar('A' + modulo) + columnName;
                columnNumber = (columnNumber - modulo) / 26;
            }
            return columnName;
        }

        // View Word as HTML using DocumentFormat.OpenXml
        private IActionResult ViewWordAsHtml(dynamic fileHandler)
        {
            try
            {
                using (var stream = new MemoryStream(fileHandler.File))
                {
                    using (var document = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(stream, false))
                    {
                        var body = document.MainDocumentPart?.Document?.Body;
                        if (body == null)
                        {
                            return Content("Document body not found", "text/plain");
                        }

                        var html = new StringBuilder();
                        html.Append($@"
<!DOCTYPE html>
<html>
<head>
    <title>{fileHandler.FileName}</title>
    <meta charset='utf-8'>
    <link href='https://cdn.jsdelivr.net/npm/bootstrap@5.1.3/dist/css/bootstrap.min.css' rel='stylesheet'>
    <style>
        body {{ background: #f8f9fa; padding: 20px; }}
        .container {{ background: white; padding: 20px; border-radius: 8px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); max-width: 900px; }}
        .header {{ border-bottom: 2px solid #007bff; padding-bottom: 10px; margin-bottom: 20px; }}
        .filename {{ color: #007bff; font-size: 18px; font-weight: bold; }}
        .document-content {{ line-height: 1.6; }}
        .document-content p {{ margin-bottom: 1rem; }}
        .document-content h1 {{ font-size: 1.5rem; margin: 1.5rem 0 1rem 0; }}
        .document-content h2 {{ font-size: 1.3rem; margin: 1.3rem 0 0.8rem 0; }}
        .document-content h3 {{ font-size: 1.1rem; margin: 1.1rem 0 0.6rem 0; }}
        .document-content table {{ width: 100%; margin: 1rem 0; }}
        .document-content table td, .document-content table th {{ padding: 8px; border: 1px solid #dee2e6; }}
        .document-content ul, .document-content ol {{ margin: 1rem 0; padding-left: 2rem; }}
        .bold {{ font-weight: bold; }}
        .italic {{ font-style: italic; }}
        .underline {{ text-decoration: underline; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <div class='filename'>📝 {fileHandler.FileName}</div>
            <small class='text-muted'>Word Document</small>
        </div>
        <div class='document-content'>");

                        // Process document elements
                        foreach (var element in body.Elements())
                        {
                            html.Append(ConvertWordElementToHtml(element));
                        }

                        html.Append(@"
        </div>
    </div>
</body>
</html>");

                        return Content(html.ToString(), "text/html");
                    }
                }
            }
            catch (Exception ex)
            {
                return Content($@"
<!DOCTYPE html>
<html>
<head>
    <title>{fileHandler.FileName}</title>
    <meta charset='utf-8'>
    <link href='https://cdn.jsdelivr.net/npm/bootstrap@5.1.3/dist/css/bootstrap.min.css' rel='stylesheet'>
</head>
<body>
    <div class='container mt-4'>
        <div class='alert alert-danger'>
            <h4>📝 Word Document: {fileHandler.FileName}</h4>
            <p>Error reading Word document: {ex.Message}</p>
            <p><a href='#' onclick='window.close()'>Close this tab</a> and try downloading the file instead.</p>
        </div>
    </div>
</body>
</html>", "text/html");
            }
        }

        // Convert Word elements to HTML
        private string ConvertWordElementToHtml(DocumentFormat.OpenXml.OpenXmlElement element)
        {
            switch (element)
            {
                case DocumentFormat.OpenXml.Wordprocessing.Paragraph para:
                    return ConvertParagraphToHtml(para);
                
                case DocumentFormat.OpenXml.Wordprocessing.Table table:
                    return ConvertTableToHtml(table);
                
                default:
                    return ""; // Skip unsupported elements
            }
        }

        // Convert Word paragraph to HTML
        private string ConvertParagraphToHtml(DocumentFormat.OpenXml.Wordprocessing.Paragraph paragraph)
        {
            var html = new StringBuilder();
            var text = new StringBuilder();
            var hasContent = false;

            foreach (var run in paragraph.Elements<DocumentFormat.OpenXml.Wordprocessing.Run>())
            {
                var runText = "";
                var isBold = false;
                var isItalic = false;
                var isUnderline = false;

                // Check formatting
                if (run.RunProperties != null)
                {
                    isBold = run.RunProperties.Bold != null;
                    isItalic = run.RunProperties.Italic != null;
                    isUnderline = run.RunProperties.Underline != null;
                }

                // Get text content
                foreach (var textElement in run.Elements())
                {
                    if (textElement is DocumentFormat.OpenXml.Wordprocessing.Text txt)
                    {
                        runText += txt.Text;
                        hasContent = true;
                    }
                    else if (textElement is DocumentFormat.OpenXml.Wordprocessing.Break)
                    {
                        runText += "<br>";
                        hasContent = true;
                    }
                }

                // Apply formatting
                if (!string.IsNullOrEmpty(runText))
                {
                    if (isBold) runText = $"<span class='bold'>{runText}</span>";
                    if (isItalic) runText = $"<span class='italic'>{runText}</span>";
                    if (isUnderline) runText = $"<span class='underline'>{runText}</span>";
                    
                    text.Append(runText);
                }
            }

            if (hasContent)
            {
                var paragraphText = text.ToString();
                
                // Check if it's a heading style (basic detection)
                var isHeading = false;
                if (paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value != null)
                {
                    var styleId = paragraph.ParagraphProperties.ParagraphStyleId.Val.Value.ToLower();
                    if (styleId.Contains("heading") || styleId.Contains("title"))
                    {
                        isHeading = true;
                        if (styleId.Contains("1"))
                            html.Append($"<h2>{System.Web.HttpUtility.HtmlEncode(paragraphText)}</h2>");
                        else if (styleId.Contains("2"))
                            html.Append($"<h3>{System.Web.HttpUtility.HtmlEncode(paragraphText)}</h3>");
                        else
                            html.Append($"<h4>{System.Web.HttpUtility.HtmlEncode(paragraphText)}</h4>");
                    }
                }
                
                if (!isHeading)
                {
                    html.Append($"<p>{paragraphText}</p>");
                }
            }

            return html.ToString();
        }

        // Convert Word table to HTML
        private string ConvertTableToHtml(DocumentFormat.OpenXml.Wordprocessing.Table table)
        {
            var html = new StringBuilder();
            html.Append("<table class='table table-bordered'>");

            foreach (var row in table.Elements<DocumentFormat.OpenXml.Wordprocessing.TableRow>())
            {
                html.Append("<tr>");
                
                foreach (var cell in row.Elements<DocumentFormat.OpenXml.Wordprocessing.TableCell>())
                {
                    html.Append("<td>");
                    
                    foreach (var paragraph in cell.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>())
                    {
                        var cellText = "";
                        foreach (var run in paragraph.Elements<DocumentFormat.OpenXml.Wordprocessing.Run>())
                        {
                            foreach (var text in run.Elements<DocumentFormat.OpenXml.Wordprocessing.Text>())
                            {
                                cellText += text.Text;
                            }
                        }
                        
                        if (!string.IsNullOrEmpty(cellText))
                        {
                            html.Append(System.Web.HttpUtility.HtmlEncode(cellText));
                        }
                    }
                    
                    html.Append("</td>");
                }
                
                html.Append("</tr>");
            }

            html.Append("</table>");
            return html.ToString();
        }

        // View PowerPoint as HTML - info page for now
        private IActionResult ViewPowerPointAsHtml(dynamic fileHandler)
        {
            // Extract URI from request URL for download link
            var currentUri = HttpContext.Request.Query["id"].ToString();
            
            return Content($@"
<!DOCTYPE html>
<html>
<head>
    <title>{fileHandler.FileName}</title>
    <meta charset='utf-8'>
    <link href='https://cdn.jsdelivr.net/npm/bootstrap@5.1.3/dist/css/bootstrap.min.css' rel='stylesheet'>
    <style>
        body {{ background: #f8f9fa; padding: 20px; }}
        .container {{ background: white; padding: 20px; border-radius: 8px; box-shadow: 0 2px 10px rgba(0,0,0,0.1); }}
        .download-btn {{ margin-top: 20px; }}
        .file-info {{ background: #e3f2fd; padding: 15px; border-radius: 8px; margin: 15px 0; }}
    </style>
</head>
<body>
    <div class='container mt-4'>
        <div class='text-center'>
            <i class='fas fa-file-powerpoint fa-3x text-warning mb-3'></i>
            <h4>📊 PowerPoint Presentation</h4>
            <h5 class='text-primary'>{fileHandler.FileName}</h5>
        </div>
        
        <div class='file-info'>
            <h6>📋 File Information</h6>
            <ul>
                <li><strong>File Type:</strong> PowerPoint Presentation</li>
                <li><strong>Size:</strong> {Math.Round(fileHandler.File.Length / 1024.0 / 1024.0, 2)} MB</li>
                <li><strong>Best Viewed In:</strong> PowerPoint, Google Slides, or compatible presentation software</li>
            </ul>
        </div>

        <div class='alert alert-info'>
            <h6><i class='fas fa-info-circle'></i> How to View This Presentation</h6>
            <p class='mb-2'>PowerPoint files are best viewed in presentation software. Choose an option below:</p>
            <ol>
                <li><strong>Download and Open:</strong> Download the file and open it with PowerPoint or similar software</li>
                <li><strong>Online Viewing:</strong> Upload to Google Drive, OneDrive, or similar cloud service for online viewing</li>
                <li><strong>Preview:</strong> Some file managers provide presentation previews</li>
            </ol>
        </div>

        <div class='text-center download-btn'>
            <form method='POST' action='/Search/DownloadSelected' style='display: inline;'>
                <input type='hidden' name='selectedIds' value='{currentUri}' />
                <button type='submit' class='btn btn-primary btn-lg'>
                    <i class='fas fa-download me-2'></i>Download Presentation
                </button>
            </form>
            
            <a href='#' onclick='window.close()' class='btn btn-secondary btn-lg ms-3'>
                <i class='fas fa-times me-2'></i>Close Tab
            </a>
        </div>

        <div class='mt-4'>
            <small class='text-muted'>
                <i class='fas fa-lightbulb'></i> 
                <strong>Tip:</strong> For the best experience, download and open this file in PowerPoint or upload it to Google Slides for online viewing.
            </small>
        </div>
    </div>
</body>
</html>", "text/html");
        }

        // Helper method to determine if file can be viewed in browser
        private bool IsViewableInBrowser(string fileName)
        {
            var extension = Path.GetExtension(fileName)?.ToLowerInvariant();
            
            return extension switch
            {
                ".pdf" => true,
                ".txt" => true,
                ".csv" => true,
                ".json" => true,
                ".xml" => true,
                ".jpg" or ".jpeg" => true,
                ".png" => true,
                ".gif" => true,
                ".bmp" => true,
                ".tiff" or ".tif" => true,
                _ => false
            };
        }

        // Serve the prepared ZIP file
        [HttpGet]
        public IActionResult DownloadZipFile(string fileName)
        {
            try
            {
                var tempDir = Path.Combine(Path.GetTempPath(), "Downloads");
                var filePath = Path.Combine(tempDir, fileName);
                
                if (System.IO.File.Exists(filePath))
                {
                    var fileBytes = System.IO.File.ReadAllBytes(filePath);
                    
                    // Clean up temp file after reading
                    try
                    {
                        System.IO.File.Delete(filePath);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Could not delete temp file {filePath}: {ex.Message}");
                    }
                    
                    return File(fileBytes, "application/zip", fileName);
                }
                else
                {
                    TempData["ErrorMessage"] = "Download file not found or has expired.";
                    return RedirectToAction("Index");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Download file error: {ex.Message}");
                TempData["ErrorMessage"] = $"Download failed: {ex.Message}";
                return RedirectToAction("Index");
            }
        }


        // Model for export request
        public class ExportRequest
        {
            public List<ExportRecord> Records { get; set; }
            public string FilePath { get; set; }
        }


        public class ExportRecord
        {
            public string Uri { get; set; }
            public string ClientId { get; set; }
            public string Title { get; set; }
            public string Container { get; set; }
            public string Country { get; set; }
            public string Region { get; set; }
            public string BillTo { get; set; }
            public string ShipTo { get; set; }
        }

        // New methods for URI-based analytics
        [HttpPost]
        public async Task<IActionResult> GetFileSummaryByURI([FromBody] FileSummaryByURIRequest request)
        {
            try
            {
                Console.WriteLine($"GetFileSummaryByURI called with URI: {request?.Uri}");

                if (request == null)
                {
                    Console.WriteLine("Request is null");
                    return BadRequest(new { error = "Request is null" });
                }

                if (request.Uri <= 0)
                {
                    Console.WriteLine("URI is invalid");
                    return BadRequest(new { error = "Valid URI is required" });
                }

                // Download the file using the existing method
                var fileHandler = _contentManager.Download((int)request.Uri);
                if (fileHandler == null || fileHandler.File == null)
                {
                    Console.WriteLine($"Failed to download file for URI: {request.Uri}");
                    return BadRequest(new { error = "Failed to download file" });
                }

                var summary = await _chatMLService.GetFileSummaryAsync(fileHandler.LocalDownloadPath);
                Console.WriteLine($"Summary generated: {summary?.Length} characters");

                // Clean up temp file
                if (System.IO.File.Exists(fileHandler.LocalDownloadPath))
                {
                    System.IO.File.Delete(fileHandler.LocalDownloadPath);
                }

                return Json(new { summary = summary });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in GetFileSummaryByURI: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return Json(new { error = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> AIAnalyticsByURI([FromBody] AIAnalyticsByURIRequest request)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            try
            {
                Console.WriteLine($"AIAnalyticsByURI called with URI: {request?.Uri}, Query: {request?.Query}");

                if (request == null)
                {
                    return BadRequest(new { error = "Request is null" });
                }

                if (string.IsNullOrEmpty(request.Query))
                {
                    return BadRequest(new { error = "Query is required" });
                }

                if (request.Uri <= 0)
                {
                    return BadRequest(new { error = "Valid URI is required" });
                }

                // Download the file using the existing method
                var downloadStopwatch = System.Diagnostics.Stopwatch.StartNew();
                var fileHandler = _contentManager.Download((int)request.Uri);
                downloadStopwatch.Stop();
                Console.WriteLine($"File download took: {downloadStopwatch.ElapsedMilliseconds}ms ({downloadStopwatch.Elapsed.TotalMinutes:F2} min)");

                if (fileHandler == null || fileHandler.File == null)
                {
                    Console.WriteLine($"Failed to download file for URI: {request.Uri}");
                    return BadRequest(new { error = "Failed to download file" });
                }

                var extractionStopwatch = System.Diagnostics.Stopwatch.StartNew();
                string fileContent = "";
                fileContent = FileTextExtractor.ExtractTextFromFile(fileHandler.LocalDownloadPath);
                extractionStopwatch.Stop();
                Console.WriteLine($"Text extraction took: {extractionStopwatch.ElapsedMilliseconds}ms ({extractionStopwatch.Elapsed.TotalMinutes:F2} min) (Content length: {fileContent.Length} chars)");

                var aiStopwatch = System.Diagnostics.Stopwatch.StartNew();
                var fileAnswer = await _helperService.GetGenerativeAnswers(request?.Query, fileHandler.LocalDownloadPath, fileContent);
                aiStopwatch.Stop();
                Console.WriteLine($"AI processing took: {aiStopwatch.ElapsedMilliseconds}ms ({aiStopwatch.Elapsed.TotalMinutes:F2} min)");

                // Clean up the response - remove HTML tags
                if (!string.IsNullOrEmpty(fileAnswer))
                {
                    // Remove HTML tags (like <strong>, </strong>, etc.)
                    fileAnswer = Regex.Replace(fileAnswer, "<.*?>", string.Empty);

                    // Optional: Clean up extra whitespace
                    fileAnswer = Regex.Replace(fileAnswer, @"\s+", " ").Trim();

                    // Optional: Decode HTML entities if needed
                    fileAnswer = System.Net.WebUtility.HtmlDecode(fileAnswer);
                }
                
                // Clean up temp file
                if (System.IO.File.Exists(fileHandler.LocalDownloadPath))
                {
                    System.IO.File.Delete(fileHandler.LocalDownloadPath);
                }

                stopwatch.Stop();
                Console.WriteLine($"✅ AIAnalyticsByURI completed in {stopwatch.ElapsedMilliseconds}ms ({stopwatch.Elapsed.TotalMinutes:F2} min) total (Answer length: {fileAnswer?.Length ?? 0} chars)");

                return Json(new { answer = fileAnswer });
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                Console.WriteLine($"❌ AIAnalyticsByURI failed after {stopwatch.ElapsedMilliseconds}ms ({stopwatch.Elapsed.TotalMinutes:F2} min) - Error: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return Json(new { error = ex.Message });
            }
        }

        // Request models for URI-based analytics
        public class FileSummaryByURIRequest
        {
            public long Uri { get; set; }
        }

        public class AIAnalyticsByURIRequest
        {
            public long Uri { get; set; }
            public string Query { get; set; }
        }

    }
}
