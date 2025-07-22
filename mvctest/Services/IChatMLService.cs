namespace mvctest.Services
{
    public interface IChatMLService
    {
        void TrainModel();
        Task<string> GetChatBotResponse(string userMessage,bool isFromGPT= false,bool isFromDeepseek =false);
        Task<string> GetChatGptResponseAsync(string userInput, string? filePath = null);
    }
}
