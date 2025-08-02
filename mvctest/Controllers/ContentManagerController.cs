using DocumentFormat.OpenXml.Drawing;
using Microsoft.AspNetCore.Mvc;
using mvctest.Models;
using mvctest.Services;
using System.IO.Compression;

namespace mvctest.Controllers
{
    public class ContentManagerController : Controller
    {
        private readonly ILuceneInterface _luceneInterface;
        private readonly IContentManager _contentManager;
        public ContentManagerController(IContentManager contentManager, ILuceneInterface luceneInterface)
        {
            _contentManager = contentManager;
            _luceneInterface = luceneInterface;
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
              //var records =  _contentManager.GetAllRecords("*");
              //  var allFiles = new List<string?>();
              //  foreach (var recRecord in records)
              //  {
              //      var data = _contentManager.Download(Convert.ToInt32(recRecord.URI));

              //      if (data != null && !string.IsNullOrEmpty(data.LocalDownloadPath))
              //      {
              //          allFiles.Add(data.LocalDownloadPath);
              //      }
              //      else
              //      {
              //          Console.WriteLine($"Skipped record {recRecord.URI} - no electronic document.");
              //      }

              //  }
               // _luceneInterface.BatchIndexFilesFromContentManager(allFiles);
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
                var searchResults = _luceneInterface.SearchFiles(content);
                return View(searchResults); // Pass list to the view
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Search failed: {ex.Message}";
                return RedirectToAction("Error", "Home", new { message = ex.Message });
            }
        }





    }
}
