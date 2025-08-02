using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using mvctest.Models;
using mvctest.Services;

namespace mvctest.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IContentManager _contentManager;

        public HomeController(ILogger<HomeController> logger, IContentManager contentManager)
        {
            _logger = logger;
            _contentManager = contentManager;
        }
        public IActionResult AccessLog(string DataSetId, string WorkGroupUrl)
        {
            var access = _contentManager.AccessLog(DataSetId, WorkGroupUrl);
            if (access)
            {
                return RedirectToAction("Index", "ContentManager");
            }

            // ❌ Access failed, show message
            ViewBag.ErrorMessage = "Failed to connect to Content Manager. Please check the DataSet ID and WorkGroup URL.";
            return View("AccessError");
        }
        public IActionResult Index()
        {
            return View();
        }
        [HttpGet]
        public IActionResult Search(string keyword)
        {
            var result = _contentManager.GetRecordByTitle(keyword);
            return Json(result.Title);
        }
        public IActionResult Privacy()
        {

            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            ViewBag.ErrorMessage = TempData["ErrorMessage"] as string;

            var model = new ErrorViewModel
            {
                RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier
            };
            return View(model);
        }



    }
}
