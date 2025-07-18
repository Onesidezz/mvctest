namespace mvctest.Services
{
    public interface IChatMLService
    {
        void TrainModel();
        Task<string> GetChatBotResponse(string userMessage);
        Task<string> GetChatGptResponseAsync(string userInput, string? filePath = null);
    }
}
