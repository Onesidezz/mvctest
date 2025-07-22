using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using mvctest.Models;
using mvctest.Services;
using System.IO.Compression;

namespace mvctest.Controllers
{
    public class ContentManagerController : Controller
    {
        private readonly IContentManager _contentManager;
        public ContentManagerController(IContentManager contentManager)
        {
            _contentManager = contentManager;
        }

        public IActionResult Index()
        {
            try
            {
                var list = _contentManager.GetAllRecords("*");
                return View(list);
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
        


    }
}
