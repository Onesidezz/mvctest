using Microsoft.AspNetCore.Mvc;
using mvctest.Services;
using static mvctest.Models.ChatBot;

namespace mvctest.Controllers
{
    public class ChatBotController : Controller
    {
        private readonly IChatMLService _chatMLService;

        public ChatBotController(IChatMLService chatMLService)
        {
            _chatMLService = chatMLService;
        }
        public ActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<ActionResult> GetResponse(ChatInput input)
        {
            var response = await _chatMLService.GetChatBotResponse(input.UserMessage);
            return Json(new ChatResponse { ResponseMessage = response });
        }
        
    }
}
