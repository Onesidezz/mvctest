using mvctest.Models;
using TRIM.SDK;

namespace mvctest.Services
{
    public interface ICachedCount
    {
        int GetTotalRecordCountCached(string searchString, Database database);
        void ProcessContainerRecord(Record record, RecordViewModel viewModel,
        List<RecordViewModel> listOfRecords, List<ContainerRecordsInfo> containerRecordsInfoList, Database database);
    }
}
