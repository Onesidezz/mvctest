namespace mvctest.Services
{
    public interface IChatMLService
    {
        Task TrainModelAsync();
        Task<string> GetChatBotResponse(string userMessage,bool isFromGPT= false,bool isFromDeepseek =false);
        Task<string> GetChatGptResponseAsync(string userInput, string? filePath = null, string prompt = "");
    }
}
