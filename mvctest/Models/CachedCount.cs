using mvctest.Models;
using mvctest.Services;
using TRIM.SDK;



public  class CachedCount : ICachedCount
{
  
    public readonly Dictionary<string, CachedCount> _countCache = new Dictionary<string, CachedCount>();
    public readonly object _lockObject = new object();
    public const int COUNT_CACHE_EXPIRY_MINUTES = 30; // Cache for 30 minutes for "*" searches

  
    public CachedCount()
    {
            
    }

    public int Count { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsExpired => DateTime.Now.Subtract(CreatedAt).TotalMinutes > COUNT_CACHE_EXPIRY_MINUTES;


    public int GetTotalRecordCountCached(string searchString, Database database)
    {
        // For wildcard searches, use longer cache duration
        var cacheKey = searchString == "*" ? "all_records_count" : $"count:{searchString}";

        lock (_lockObject)
        {
            // Check cache first
            if (_countCache.ContainsKey(cacheKey) && !_countCache[cacheKey].IsExpired)
            {
                return _countCache[cacheKey].Count;
            }

            // Get fresh count from TRIM
            TrimMainObjectSearch countSearch = new TrimMainObjectSearch(database, BaseObjectTypes.Record);

            if (searchString != "*" && !string.IsNullOrWhiteSpace(searchString))
            {
                countSearch.SetSearchString($"typedTitle:{searchString}");
            }

            int count = 0;
            // Just iterate to count - don't process the records
            foreach (Record record in countSearch)
            {
                count++;
            }

            // Cache the result
            _countCache[cacheKey] = new CachedCount
            {
                Count = count,
                CreatedAt = DateTime.Now
            };

            return count;
        }
    }

    public void ProcessContainerRecord(Record record, RecordViewModel viewModel,
        List<RecordViewModel> listOfRecords, List<ContainerRecordsInfo> containerRecordsInfoList, Database database)
    {
        var contentLines = record.Contents
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        var containerName = record.Title;
        var containerRecordNested = new ContainerRecordsInfo
        {
            ContainerName = containerName
        };

        if (viewModel.ContainerCount == null)
        {
            viewModel.ContainerCount = new Dictionary<string, long>();
        }

        viewModel.ContainerCount[containerName] = contentLines.Length;

        // Process nested records efficiently
        foreach (var line in contentLines)
        {
            var parts = line.Split(':');
            string nestedTitle = parts.Length > 1 ? parts[1].Trim() : line.Trim();
            containerRecordNested.ChildTitles.Add(nestedTitle);

            var nestedRecord = GetRecordByTitle(nestedTitle, database);

            if (nestedRecord != null)
            {
                listOfRecords.Add(new RecordViewModel
                {
                    URI = nestedRecord.Uri.Value,
                    Title = nestedRecord.Title,
                    Container = record.Title,
                    AllParts = nestedRecord.AllParts ?? "",
                    Assignee = nestedRecord.Assignee?.Name ?? "",
                    DateCreated = nestedRecord.DateCreated.ToShortDateString(),
                    IsContainer = "Child Document",
                    ACL = nestedRecord.AccessControlList
                });
            }
            else
            {
                listOfRecords.Add(new RecordViewModel
                {
                    URI = 0,
                    Title = nestedTitle,
                    Container = record.Title,
                    AllParts = "",
                    Assignee = database.CurrentUser.Name,
                    DateCreated = DateTime.Now.ToShortDateString(),
                    IsContainer = "Child (Unresolved)",
                    ACL = record.AccessControlList
                });
            }
        }

        containerRecordsInfoList.Add(containerRecordNested);
    }

    public Record GetRecordByTitle(string title, Database database)
    {
        TrimMainObjectSearch search = new TrimMainObjectSearch(database, BaseObjectTypes.Record);
        search.SetSearchString($"typedTitle:{title}");

        foreach (Record record in search)
        {
            if (record.Title.Equals(title, StringComparison.OrdinalIgnoreCase))
            {
                return record;
            }
        }

        return null;
    }
}

