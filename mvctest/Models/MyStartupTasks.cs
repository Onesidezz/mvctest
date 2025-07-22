using Microsoft.Extensions.Options;
using mvctest.Services;

namespace mvctest.Models
{
    public class MyStartupTasks : IStartupFunctionalities
    {
        private readonly IContentManager _contentManager;
        private readonly AppSettings _appSettings;
        private readonly IChatMLService _chatMLService;

        public MyStartupTasks(IContentManager contentManager, IOptions<AppSettings> options, IChatMLService chatMLService)
        {
            _contentManager = contentManager;
            _appSettings = options.Value;
            _chatMLService = chatMLService;
        }
       

        public  async void StartupFunctionalities()
        {
            _contentManager.EnsureConnected();
            if (!File.Exists(_appSettings.TraningDataPath))
            {
                var list = _contentManager.GetAllRecords("*");
                _contentManager.GenerateChatTrainingDataCsv(list, _appSettings.TraningDataPath);
                _contentManager.AppendAdvancedChatTrainingData(list, _appSettings.TraningDataPath);
            }
            if (!File.Exists(_appSettings.TrainedModelPath))
                await _chatMLService.TrainModelAsync();
        }
    }
}
