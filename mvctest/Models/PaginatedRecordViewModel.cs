namespace mvctest.Models
{
    public class PaginatedRecordViewModel
    {
        public List<RecordViewModel> Records { get; set; } = new List<RecordViewModel>();
        public int CurrentPage { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public int TotalRecords { get; set; } = 0;

        public int TotalPages => TotalRecords > 0 ? (int)Math.Ceiling((double)TotalRecords / PageSize) : 1;

        // Additional properties for better pagination
        public bool HasPreviousPage => CurrentPage > 1;
        public bool HasNextPage => CurrentPage < TotalPages;
        public int StartRecord => (CurrentPage - 1) * PageSize + 1;
        public int EndRecord => Math.Min(CurrentPage * PageSize, TotalRecords);
    }

    public class PaginatedResult<T>
    {
        public List<T> Records { get; set; } = new List<T>();
        public int TotalRecords { get; set; } = 0;
    }
}
