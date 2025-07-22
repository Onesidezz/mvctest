using mvctest.Models;
using TRIM.SDK;

namespace mvctest.Services
{
    public interface IContentManager
    {
        void EnsureConnected();
        void ConnectDataBase(String dataSetId, String workGroupServerUrl);
        Record GetRecordByTitle(string title);
        List<RecordViewModel> GetAllRecords(string all);
        RecordViewModel GetRecordwithURI(int number);
        FileHandaler Download(int id);
        bool DeleteRecord(int uri);
        bool AccessLog(string DatasetId, string WorkGroupURL);
        void GenerateChatTrainingDataCsv(List<RecordViewModel> records, string filePath);
        void AppendAdvancedChatTrainingData(List<RecordViewModel> records, string filePath);
        bool CreateRecord(CreateRecord recors);
    }
}
