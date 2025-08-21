using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using mvctest.Models;
using mvctest.Services;
using System.IO;

namespace mvctest.Controllers
{
    public class SearchController : Controller
    {
        private readonly IContentManager _contentManager;
        private readonly ISessionSearchManager _sessionSearchManager;

        public SearchController(IContentManager contentManager, ISessionSearchManager sessionSearchManager)
        {
            _contentManager = contentManager;
            _sessionSearchManager = sessionSearchManager;
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

                // Create search session using the service
                var searchId = await _sessionSearchManager.CreateSearchSessionAsync(search, HttpContext);

                // Execute search using ContentManager  
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

    }
}
