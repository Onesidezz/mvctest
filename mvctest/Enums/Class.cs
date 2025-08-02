namespace mvctest.Enums
{
    public enum ProcessingStatus
    {
        Queued,
        Started,
        ReadingCSV,
        CheckingExisting,
        Processing,
        Completed,
        Failed,
        Cancelled,
        NotFound
    }
}
