using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using mvctest.Models;
using mvctest.Services;

namespace mvctest.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProcessingController : ControllerBase
    {
        private readonly ILuceneInterface _luceneInterface;
        public ProcessingController(ILuceneInterface luceneInterface)
        {
            _luceneInterface = luceneInterface;
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

    }
}
