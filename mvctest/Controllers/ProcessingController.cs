using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using mvctest.Models;
using mvctest.Services;

namespace mvctest.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProcessingController : ControllerBase
    {
        private readonly ILuceneInterface _luceneInterface;
        private readonly IContentManager _contentManager;
        private readonly AppSettings _settings;
        
        public ProcessingController(ILuceneInterface luceneInterface, IContentManager contentManager, IOptions<AppSettings> settings)
        {
            _luceneInterface = luceneInterface;
            _contentManager = contentManager;
            _settings = settings.Value;
            Console.WriteLine("🚀 ProcessingController initialized with Lucene, ContentManager, and Settings");
        }
        [HttpPost("process-directory")]
        public async Task<IActionResult> ProcessDirectory([FromBody] ProcessDirectoryRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.DirectoryPath))
                {
                    return BadRequest("DirectoryPath is required");
                }

                var result = await _luceneInterface.ProcessFilesInDirectory(request.DirectoryPath);

                if (result)
                {
                    return Ok(new { message = "Directory processed successfully" });
                }
                else
                {
                    return StatusCode(500, new { message = "Failed to process directory" });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Error processing directory: {ex.Message}" });
            }
        }

        [HttpPost("index-all-records")]
        public async Task<IActionResult> IndexAllContentManagerRecords()
        {
            try
            {
                Console.WriteLine("🚀 Manual indexing API called - IndexAllContentManagerRecords");
                
                // Initialize database connection using settings
                var datasetId = _settings.DataSetID;
                var workgroupUrl = _settings.WorkGroupUrl?.Trim();
                
                if (string.IsNullOrEmpty(datasetId) || string.IsNullOrEmpty(workgroupUrl))
                {
                    return BadRequest(new { 
                        success = false,
                        message = "Database connection settings not found. Please check appsettings.json for DataSetID and WorkGroupUrl.",
                        timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                    });
                }
                
                Console.WriteLine($"📊 Initializing database connection - DatasetId: {datasetId}, WorkgroupUrl: {workgroupUrl}");
                
                // Initialize database connection for indexing
                _contentManager.InitializeDatabaseForIndexing(datasetId, workgroupUrl);
                
                // Run indexing in background task to avoid timeout
                await Task.Run(() => _contentManager.IndexAllContentManagerRecordsToLucene());
                
                return Ok(new { 
                    success = true,
                    message = "All ContentManager records have been successfully indexed to Lucene",
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error in IndexAllContentManagerRecords API: {ex.Message}");
                return StatusCode(500, new { 
                    success = false,
                    message = $"Failed to index ContentManager records: {ex.Message}",
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });
            }
        }

        [HttpGet("index-status")]
        public IActionResult GetIndexStatus()
        {
            try
            {
                // Get basic index statistics
                _luceneInterface.ShowIndexStats();
                
                return Ok(new {
                    success = true,
                    message = "Index status retrieved successfully. Check console for details.",
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new {
                    success = false,
                    message = $"Error getting index status: {ex.Message}",
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });
            }
        }

        [HttpPost("clear-index")]
        public IActionResult ClearIndex([FromBody] ClearIndexRequest request)
        {
            try
            {
                if (request?.Confirmation != "CLEAR_ALL_INDEX")
                {
                    return BadRequest(new {
                        success = false,
                        message = "Invalid confirmation. Must provide 'CLEAR_ALL_INDEX' to confirm index clearing.",
                        requiredConfirmation = "CLEAR_ALL_INDEX"
                    });
                }

                Console.WriteLine("⚠️ Manual index clearing API called");
                _luceneInterface.ClearIndex(request.Confirmation);
                
                return Ok(new {
                    success = true,
                    message = "Lucene index cleared successfully",
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error clearing index: {ex.Message}");
                return StatusCode(500, new {
                    success = false,
                    message = $"Failed to clear index: {ex.Message}",
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                });
            }
        }
    }

    // Request models
    public class ClearIndexRequest
    {
        public string Confirmation { get; set; }
    }
}
