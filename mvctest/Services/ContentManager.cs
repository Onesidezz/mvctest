using DocumentFormat.OpenXml.Drawing;
using Microsoft.Extensions.Options;
using mvctest.Context;
using mvctest.Models;
using System.Data;
using System.Globalization;
using System.Text;
using TRIM.SDK;
using static mvctest.Models.ChatBot;
using Path = System.IO.Path;

namespace mvctest.Services
{
    public class ContentManager : IContentManager
    {
        private Database database;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ContentManagerContext _dbContext;
        private readonly AppSettings _settings;
        private static bool _isConnected = false;
        private string? datasetId = null;
        private string? workgroupUrl = null;
        private readonly ICachedCount _cachedCount;
        public ContentManager(IHttpContextAccessor httpContextAccessor, ContentManagerContext dbContext, IOptions<AppSettings> options, ICachedCount cachedCount)
        {
            _httpContextAccessor = httpContextAccessor;
            _dbContext = dbContext;
            _settings = options.Value;
            datasetId = _httpContextAccessor.HttpContext?.Session.GetString("DatasetId");
            //ConnectDataBase(_settings.DataSetID, _settings.WorkGroupUrl);
            _cachedCount = cachedCount;
            EnsureConnected();
        }
        public void ConnectDataBase(String dataSetId, String workGroupServerUrl)
        {
            database = new Database()
            {
                Id = dataSetId,
                WorkgroupServerURL = workGroupServerUrl
            };
            database.Connect();
        }

        public void EnsureConnected()
        {
            datasetId = _httpContextAccessor.HttpContext?.Session.GetString("DatasetId");
            workgroupUrl = _httpContextAccessor.HttpContext?.Session.GetString("WorkGroupUrl");
            if (datasetId != null && workgroupUrl != null)
            {
                if (database != null) return;
                ConnectDataBase(datasetId, workgroupUrl);
            }
        }


        private static void CheckFunctionAccess(TrimAccessControlList accessControl, int functionIndex, string functionName)
        {
            try
            {
                AccessControlSettings currentSetting = accessControl.GetCurrentAccessControlSettings(functionIndex);
                //Console.WriteLine($"\n{functionName} (Function {functionIndex}):");
                //Console.WriteLine($"  Setting: {currentSetting}");

                // Check if current user has access
                bool hasAccess = accessControl.GetAccessAllowed(functionIndex);
                //Console.WriteLine($"  Current User Has Access: {hasAccess}");

                // Get access locations if it's private
                if (currentSetting == AccessControlSettings.Private)
                {
                    LocationList accessLocations = accessControl.GetAccessLocations(functionIndex);
                    //Console.WriteLine($"  Access granted to {accessLocations.Count} locations:");
                    foreach (Location location in accessLocations)
                    {
                        Console.WriteLine($"    - {location.FullFormattedName}");
                    }
                }

                // Get description string
                string accessDescription = accessControl.GetAsString(functionIndex);
                if (!string.IsNullOrEmpty(accessDescription))
                {
                    Console.WriteLine($"  Access Description: {accessDescription}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Error checking {functionName}: {ex.Message}");
            }
        }

        public void uk(Record record, Location location)
        {
            try
            {
                TrimAccessControlList accessControl = record.AccessControlList;
                Location userLocation = new Location(database, location.Name);

                for (int functionIndex = 0; functionIndex < 6; functionIndex++)
                {
                    bool hasAccess = accessControl.GetAccessAllowedForUser(functionIndex, userLocation);
                    // Console.WriteLine($"  Function {functionIndex}: {hasAccess}");
                }
                CheckFunctionAccess(accessControl, 0, "View Document");
                CheckFunctionAccess(accessControl, 1, "Update Document");
                CheckFunctionAccess(accessControl, 2, "Update Record Metadata");
                CheckFunctionAccess(accessControl, 3, "Modify Record Access");
                CheckFunctionAccess(accessControl, 4, "Destroy Record");
                CheckFunctionAccess(accessControl, 5, "Contribute Contents");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

        }
        public PaginatedResult<RecordViewModel> GetPaginatedRecords(string searchString, int page, int pageSize)
        {
            // For wildcard searches, use estimated count or skip total count entirely
            int totalRecords = 0;
            bool useEstimatedCount = (searchString == "*" || string.IsNullOrWhiteSpace(searchString));

            if (useEstimatedCount)
            {
                // Option 1: Use a reasonable estimate (update this value periodically)
                totalRecords = GetEstimatedTotalRecords();

                // Option 2: Or calculate total only when specifically needed
                // totalRecords = -1; // Indicates "unknown" - handle in UI
            }
            else
            {
                // For specific searches, get accurate count (should be much smaller dataset)
                totalRecords = _cachedCount.GetTotalRecordCountCached(searchString,database);
            }

            // Get paginated results using TRIM SDK pagination
            TrimMainObjectSearch search = new TrimMainObjectSearch(database, BaseObjectTypes.Record);

            if (useEstimatedCount)
            {
                search.SetSearchString($"typedTitle:{searchString}");
            }

            search.PagingMode = true;
            long skip = (page - 1) * pageSize;
            search.SkipCount = skip;
            search.LimitOnRowsReturned = pageSize;
            search.SetSortString("DateCreated");

            var listOfRecords = new List<RecordViewModel>();
            var containerRecordsInfoList = new List<ContainerRecordsInfo>();
            var processedContainerIds = new HashSet<long>();

            // Process only the paginated records
            foreach (Record record in search)
            {
                bool isContainer = !string.IsNullOrWhiteSpace(record.Contents?.Trim());

                var viewModel = new RecordViewModel
                {
                    URI = record.Uri.Value,
                    Title = record.Title,
                    Container = record.Container?.Name ?? "",
                    AllParts = record.AllParts ?? "",
                    Assignee = record.Assignee?.Name ?? "",
                    DateCreated = record.DateCreated.ToShortDateString(),
                    IsContainer = isContainer ? "Container" : "Document File",
                    ContainerCount = isContainer ? new Dictionary<string, long>() : null,
                    ACL = record.AccessControlList
                };

                listOfRecords.Add(viewModel);

                if (isContainer && !processedContainerIds.Contains(record.Uri.Value))
                {
                    processedContainerIds.Add(record.Uri.Value);
                    _cachedCount.ProcessContainerRecord(record, viewModel, listOfRecords, containerRecordsInfoList,database);
                }
            }

            // If we got fewer records than pageSize, we might be at the end
            if (useEstimatedCount && listOfRecords.Count < pageSize)
            {
                // Adjust the total count estimate based on current page
                totalRecords = ((page - 1) * pageSize) + listOfRecords.Count;
            }

            if (listOfRecords.Any())
            {
                listOfRecords[0].containerRecordsInfo = containerRecordsInfoList;
                listOfRecords[0].Totalrecords = totalRecords;
            }

            return new PaginatedResult<RecordViewModel>
            {
                Records = listOfRecords,
                TotalRecords = totalRecords
            };
        }

        private int GetEstimatedTotalRecords()
        {
          
            return _settings.EstimatedRecordCount != null
                ? _settings.EstimatedRecordCount
                : 25000; // Default estimate
        }

        private int EstimateCountBySampling()
        {
          
            TrimMainObjectSearch sampleSearch = new TrimMainObjectSearch(database, BaseObjectTypes.Record);
            sampleSearch.LimitOnRowsReturned = 100;

            int sampleCount = 0;
            DateTime? firstDate = null;
            DateTime? lastDate = null;

            foreach (Record record in sampleSearch)
            {
                sampleCount++;
                if (firstDate == null) firstDate = record.DateCreated;
                lastDate = record.DateCreated;
            }

            if (sampleCount == 100 && firstDate.HasValue && lastDate.HasValue)
            {
                // Very rough estimation based on date range
                // This is just an example - you'd need better logic
                var totalDays = (DateTime.Now - firstDate.Value).TotalDays;
                var sampleDays = (lastDate.Value - firstDate.Value).TotalDays;

                if (sampleDays > 0)
                {
                    return (int)((sampleCount / sampleDays) * totalDays);
                }
            }

            return sampleCount > 0 ? sampleCount * 250 : 25000; // Rough estimate
        }
        //public PaginatedResult<RecordViewModel> GetPaginatedRecords(string searchString, int page, int pageSize)
        //{
        //    TrimMainObjectSearch search = new TrimMainObjectSearch(database, BaseObjectTypes.Record);
        //    search.SetSearchString($"typedTitle:{searchString}");
        //    //search.SetSortString("DateCreated");

        //    var listOfRecords = new List<RecordViewModel>();
        //    var containerRecordsInfoList = new List<ContainerRecordsInfo>();

        //    // First pass: Get all records and build the complete list
        //    foreach (Record record in search)
        //    {
        //        bool isContainer = !string.IsNullOrWhiteSpace(record.Contents?.Trim());

        //        var viewModel = new RecordViewModel
        //        {
        //            URI = record.Uri.Value,
        //            Title = record.Title,
        //            Container = record.Container?.Name ?? "",
        //            AllParts = record.AllParts ?? "",
        //            Assignee = record.Assignee?.Name ?? "",
        //            DateCreated = record.DateCreated.ToShortDateString(),
        //            IsContainer = isContainer ? "Container" : "Document File",
        //            ContainerCount = isContainer ? new Dictionary<string, long>() : null,
        //            ACL = record.AccessControlList
        //        };

        //        listOfRecords.Add(viewModel);

        //        if (isContainer)
        //        {
        //            var contentLines = record.Contents
        //                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        //            var containerName = record.Title;
        //            var containerRecordNested = new ContainerRecordsInfo
        //            {
        //                ContainerName = containerName
        //            };

        //            if (viewModel.ContainerCount == null)
        //            {
        //                viewModel.ContainerCount = new Dictionary<string, long>();
        //            }

        //            viewModel.ContainerCount[containerName] = contentLines.Length;

        //            foreach (var line in contentLines)
        //            {
        //                var parts = line.Split(':');
        //                string nestedTitle = parts.Length > 1 ? parts[1].Trim() : line.Trim();
        //                containerRecordNested.ChildTitles.Add(nestedTitle);

        //                var nestedRecord = GetRecordByTitle(nestedTitle);

        //                if (nestedRecord != null)
        //                {
        //                    listOfRecords.Add(new RecordViewModel
        //                    {
        //                        URI = nestedRecord.Uri.Value,
        //                        Title = nestedRecord.Title,
        //                        Container = record.Title,
        //                        AllParts = nestedRecord.AllParts ?? "",
        //                        Assignee = nestedRecord.Assignee?.Name ?? "",
        //                        DateCreated = nestedRecord.DateCreated.ToShortDateString(),
        //                        IsContainer = "Child Document",
        //                        ACL = nestedRecord.AccessControlList
        //                    });
        //                }
        //                else
        //                {
        //                    listOfRecords.Add(new RecordViewModel
        //                    {
        //                        URI = 0,
        //                        Title = nestedTitle,
        //                        Container = record.Title,
        //                        AllParts = "",
        //                        Assignee = database.CurrentUser.Name,
        //                        DateCreated = DateTime.Now.ToShortDateString(),
        //                        IsContainer = "Child (Unresolved)",
        //                        ACL = record.AccessControlList
        //                    });
        //                }
        //            }

        //            containerRecordsInfoList.Add(containerRecordNested);
        //        }
        //    }

        //    // Get total count
        //    int totalRecords = listOfRecords.Count;

        //    // Apply pagination
        //    var paginatedRecords = listOfRecords
        //        .Skip((page - 1) * pageSize)
        //        .Take(pageSize)
        //        .ToList();

        //    // Set container info on first record if exists
        //    if (paginatedRecords.Any())
        //    {
        //        paginatedRecords[0].containerRecordsInfo = containerRecordsInfoList;
        //        paginatedRecords[0].Totalrecords = totalRecords;
        //    }

        //    return new PaginatedResult<RecordViewModel>
        //    {
        //        Records = paginatedRecords,
        //        TotalRecords = totalRecords
        //    };
        //}
        public List<RecordViewModel> GetAllRecords(string all)
        {
            TrimMainObjectSearch search = new TrimMainObjectSearch(database, BaseObjectTypes.Record);
            search.SetSearchString($"typedTitle:{all}");

            var listOfRecords = new List<RecordViewModel>();
            var containerRecordsInfoList = new List<ContainerRecordsInfo>(); // Store all container info

            foreach (Record record in search)
            {
                bool isContainer = !string.IsNullOrWhiteSpace(record.Contents?.Trim());

                var viewModel = new RecordViewModel
                {
                    URI = record.Uri.Value,
                    Title = record.Title,
                    Container = record.Container?.Name ?? "",
                    AllParts = record.AllParts ?? "",
                    Assignee = record.Assignee?.Name ?? "",
                    DateCreated = record.DateCreated.ToShortDateString(),
                    IsContainer = isContainer ? "Container" : "Document File",
                    ContainerCount = isContainer ? new Dictionary<string, long>() : null,
                    ACL = record.AccessControlList
                };

                listOfRecords.Add(viewModel);

                if (isContainer)
                {
                    var contentLines = record.Contents
                        .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

                    var containerName = record.Title;
                    var containerRecordNested = new ContainerRecordsInfo
                    {
                        ContainerName = containerName
                    };

                    // Initialize ContainerCount if it's null
                    if (viewModel.ContainerCount == null)
                    {
                        viewModel.ContainerCount = new Dictionary<string, long>();
                    }

                    viewModel.ContainerCount[containerName] = contentLines.Length;

                    // Process each file/child in this container
                    foreach (var line in contentLines)
                    {
                        var parts = line.Split(':');
                        string nestedTitle = parts.Length > 1 ? parts[1].Trim() : line.Trim();
                        containerRecordNested.ChildTitles.Add(nestedTitle);

                        var nestedRecord = GetRecordByTitle(nestedTitle);

                        if (nestedRecord != null)
                        {
                            listOfRecords.Add(new RecordViewModel
                            {
                                URI = nestedRecord.Uri.Value,
                                Title = nestedRecord.Title,
                                Container = record.Title, // Parent container name
                                AllParts = nestedRecord.AllParts ?? "",
                                Assignee = nestedRecord.Assignee?.Name ?? "",
                                DateCreated = nestedRecord.DateCreated.ToShortDateString(),
                                IsContainer = "Child Document",
                                ACL = nestedRecord.AccessControlList
                            });
                        }
                        else
                        {
                            // Unresolved/missing child record
                            listOfRecords.Add(new RecordViewModel
                            {
                                URI = 0,
                                Title = nestedTitle,
                                Container = record.Title, // Parent container name
                                AllParts = "",
                                Assignee = database.CurrentUser.Name,
                                DateCreated = DateTime.Now.ToShortDateString(),
                                IsContainer = "Child (Unresolved)",
                                ACL = record.AccessControlList
                            });
                        }
                    }

                    // Add this container's info to the master list
                    containerRecordsInfoList.Add(containerRecordNested);
                }
            }

            // Set the complete container information and total records on the first record
            // This gives access to ALL container information from the entire search result
            if (listOfRecords.Any())
            {
                listOfRecords[0].containerRecordsInfo = containerRecordsInfoList;
                listOfRecords[0].Totalrecords = listOfRecords.Count;
            }

            return listOfRecords;
        }
 
        public Record GetRecordByTitle(string title)
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

        public RecordViewModel GetRecordwithURI(int number)
        {
            try
            {
                Record record = new Record(database, number);

                var viewModel = new RecordViewModel
                {
                    URI = record.Uri.Value,
                    Title = record.Title,
                    Container = record.Container?.Name ?? "",
                    AllParts = record.AllParts ?? "",
                    Assignee = record.Assignee?.Name ?? "",
                    DateCreated = record.DateCreated.ToShortDateString(),
                    IsContainer = !string.IsNullOrWhiteSpace(record.Contents) ? "Container" : "",
                    DownloadLink = record.ESource
                };
                return viewModel;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }

            return null;
        }
        public FileHandaler Download(int id)
        {
            try
            {
                var filehandeler = new FileHandaler();
                Record record = new Record(database, id);
                if (!record.IsElectronic)
                {
                    Console.WriteLine($"Record with ID {id} does not have an electronic document.");
                    return null; // Or return filehandeler with empty fields if preferred
                }
                string outputPath = @"C:\Temp\Download\" + record.Title + Path.GetExtension(record.Extension);
                string fileName = record.Title + Path.GetExtension(record.Extension);

                string savedPath = record.GetDocument(
                    outputPath,
                    false,
                    "Downloaded via app",
                    outputPath
                );
                var bytesdata = System.IO.File.ReadAllBytes(savedPath);
                filehandeler.File = bytesdata;
                filehandeler.FileName = fileName;
                filehandeler.LocalDownloadPath = outputPath;
                return filehandeler;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                throw;
            }
        }
        public bool DeleteRecord(int uri)
        {
            try
            {
                Record record = new Record(database, uri);

                if (record.IsDeleteOk())
                {
                    record.Delete();
                    database.Save();
                    return true;
                }
                else
                {
                    Console.WriteLine("You do not have permission to delete this record.");
                    return false;
                }
            }
            catch (TrimException tex)
            {
                Console.WriteLine("TRIM error: " + tex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("General error: " + ex.Message);
            }

            return false;
        }


        public void AddRecordRelationship(string recordNumberA, string recordNumberB, RecordRelationshipType relationshipType)
        {
            try
            {
                // Load the two records
                Record recordA = new Record(database, recordNumberA);
                Record recordB = new Record(database, recordNumberB);

                // Add relationship: recordA.RelateTo(recordB, relationshipType);
                recordA.AttachRelationship(recordB, relationshipType);

                //// Add a 'Copy Of' relationship (temp copy)
                AddRecordRelationship("DOC1001", "DOC1002", RecordRelationshipType.IsTempCopy);

                //// Add a 'Supersedes' relationship
                //AddRecordRelationship("DOC1003", "DOC1004", RecordRelationshipType.DoesSupersede);


                // Save the changes to recordA
                recordA.Save();

                Console.WriteLine($"Successfully added relationship '{relationshipType}' between {recordNumberA} and {recordNumberB}");

            }
            catch (TrimException ex)
            {
                Console.WriteLine($"Error creating relationship: {ex.Message}");
            }
            finally
            {

            }
        }


        public void GenerateChatTrainingDataCsv(List<RecordViewModel> records, string filePath)
        {
            Console.WriteLine("Inside GenerateChatTrainingDataCsv function");

            var trainingData = new List<ChatData>();
            // Static greetings/intents
            trainingData.AddRange(new[]
            {
                new ChatData { Text = "hi", Label = $"Hello! {database.CurrentUser.FormattedName}" },
                new ChatData { Text = "hello", Label = $"Hello! {database.CurrentUser.FormattedName}" },
                new ChatData { Text = "how are you", Label = "I'm good, thank you!" },
                new ChatData { Text = "bye", Label = "Goodbye!" },
                new ChatData { Text = "who are you", Label = "I am your assistant!" },
                new ChatData { Text = "what is your name", Label = "I'm Umar" },
                new ChatData { Text = "thanks", Label = "You're welcome!" },
                new ChatData { Text = "what time is it", Label = "Sorry, I can't tell time right now." },
                new ChatData { Text = "good morning", Label = "Good morning!" },
                new ChatData { Text = "good night", Label = "Good night!" }
            });

            // ✅ 1. Total record training
            long totalRecords = records.FirstOrDefault()?.Totalrecords ?? records.Count;
            var totalVariants = new[] { "total record", "how many total records", "total records count", "total count", "total document count" };
            foreach (var phrase in totalVariants)
            {
                trainingData.Add(new ChatData { Text = phrase, Label = $"There are {totalRecords} records in total." });
            }

            // ✅ 2. Container-based training (using ContainerCount + Child Titles)

            var containerDetailsDict = new Dictionary<string, ContainerRecordsInfo>();

            foreach (var rec in records)
            {
                // Only process actual container records (not child documents)
                if (rec.IsContainer == "Container" && rec.containerRecordsInfo != null)
                {
                    foreach (var containerInfo in rec.containerRecordsInfo)
                    {
                        if (!string.IsNullOrWhiteSpace(containerInfo.ContainerName))
                        {
                            containerDetailsDict[containerInfo.ContainerName] = containerInfo;
                        }
                    }
                }
            }

            foreach (var kv in containerDetailsDict)
            {
                var container = kv.Key;
                var info = kv.Value;
                var count = info.ChildTitles.Count;

                // Format child record titles
                var childListFormatted = string.Join(" ", info.ChildTitles.Select((title, index) => $"{index + 1}. {title}"));

                var label = $"{container} contains {count} records with:{childListFormatted}";

                var containerPrompts = new[]
                {
                    $"What is in {container}?",
                    $"How many records are in {container}?",
                    $"Tell me about {container}",
                    $"{container}",
                    $"{container} folder",
                    $"{container} container"
                };

                foreach (var prompt in containerPrompts)
                {
                    trainingData.Add(new ChatData
                    {
                        Text = prompt,
                        Label = label
                    });
                }
            }


            // ✅ 3. DateCreated-based combinations
            var groupedByDate = records
                .Where(r => !string.IsNullOrEmpty(r.DateCreated))
                .GroupBy(r => r.DateCreated!.Trim())
                .ToDictionary(g => g.Key, g => g.Count());

            foreach (var kv in groupedByDate)
            {
                var date = kv.Key;
                var count = kv.Value;
                var prompts = new[]
                {
                $"How many records were created on {date}?",
                $"Records created on {date}",
                $"Count of records on {date}",
                $"How many files created on {date}?"
            };

                foreach (var prompt in prompts)
                {
                    trainingData.Add(new ChatData
                    {
                        Text = prompt,
                        Label = $"There are {count} records created on {date}."
                    });
                }
            }

            // ✅ 4. Record-based training
            foreach (var record in records)
            {
                string name = record.Title?.Trim();
                string container = record.Container?.Trim();
                string type = record.IsContainer?.Trim();
                string assignee = record.Assignee?.Trim();
                string date = record.DateCreated?.Trim();

                // Skip if title is empty
                if (string.IsNullOrEmpty(name)) continue;

                // Per record training
                trainingData.Add(new ChatData { Text = $"Tell me about {name}", Label = $"{name} is a {type} created on {date} by {assignee}." });
                trainingData.Add(new ChatData { Text = $"What is {name}?", Label = $"{name} is a {type} owned by {assignee}." });
                trainingData.Add(new ChatData { Text = $"Who owns {name}?", Label = $"{name} is assigned to {assignee}." });
                trainingData.Add(new ChatData { Text = $"When was {name} created?", Label = $"{name} was created on {date}." });
                trainingData.Add(new ChatData { Text = $"Is {name} a file or container?", Label = $"{name} is a {type}." });

                // If container is available
                if (!string.IsNullOrEmpty(container) && container != name)
                {
                    trainingData.Add(new ChatData { Text = $"Tell me about the container {container}", Label = $"{name} is inside the container {container}, created on {date} by {assignee}." });
                    trainingData.Add(new ChatData { Text = $"What is in {container}?", Label = $"{container} contains {name}, which is a {type} assigned to {assignee}." });
                    trainingData.Add(new ChatData { Text = $"Who owns the container {container}?", Label = $"{container} contains {name}, which is assigned to {assignee}." });
                    trainingData.Add(new ChatData { Text = $"When was the container {container} updated?", Label = $"It contains {name} which was created on {date}." });
                }
            }

            // ✅ Write to CSV
            using (var writer = new StreamWriter(filePath, false, Encoding.UTF8))
            using (var csv = new CsvHelper.CsvWriter(writer, CultureInfo.InvariantCulture))
            {
                csv.WriteHeader<ChatData>();
                csv.NextRecord();

                foreach (var item in trainingData)
                {
                    csv.WriteRecord(item);
                    csv.NextRecord();
                }
            }

            Console.WriteLine($"Training data saved to: {filePath}");
        }
        public void AppendAdvancedChatTrainingData(List<RecordViewModel> records, string filePath)
        {
            Console.WriteLine("Inside AppendAdvancedChatTrainingData function");

            try
            {
                // Safe date parsing and ordering
                var validDateRecords = new List<(RecordViewModel Record, DateTime ParsedDate)>();
                foreach (var r in records)
                {
                    if (DateTime.TryParse(r.DateCreated, out var parsedDate))
                    {
                        validDateRecords.Add((r, parsedDate));
                    }
                    else
                    {
                        Console.WriteLine($"Invalid DateCreated: {r.DateCreated}");
                    }
                }

                validDateRecords = validDateRecords.OrderBy(r => r.ParsedDate).ToList();

                int trainingCount = 0;

                // Open stream once and write directly
                using (var stream = new StreamWriter(filePath, true, Encoding.UTF8))
                using (var csv = new CsvHelper.CsvWriter(stream, CultureInfo.InvariantCulture))
                {
                    // Date range-based training data (streamed)
                    for (int i = 0; i < validDateRecords.Count; i++)
                    {
                        for (int j = i; j < validDateRecords.Count; j++)
                        {
                            var start = validDateRecords[i].ParsedDate;
                            var end = validDateRecords[j].ParsedDate;

                            var inRange = validDateRecords
                                .Where(r => r.ParsedDate >= start && r.ParsedDate <= end)
                                .Select(r => r.Record)
                                .ToList();

                            if (inRange.Count < 1) continue;

                            string rangeText = $"{start:MM/dd/yyyy} to {end:MM/dd/yyyy}";
                            string recordList = string.Join(" ", inRange.Select((r, index) => $"{index + 1}. {r.Title?.Trim()}"));
                            string label = $"There are {inRange.Count} records created between {rangeText} including: {recordList}";

                            csv.WriteRecord(new ChatData
                            {
                                Text = $"Show records between {rangeText}",
                                Label = label
                            });
                            csv.NextRecord();
                            trainingCount++;

                            if (trainingCount % 1000 == 0)
                                Console.WriteLine($"Written training data count: {trainingCount}");
                        }
                    }

                    Console.WriteLine($"Date range training entries written: {trainingCount}");

                    // Multi-entity queries
                    foreach (var record in records)
                    {
                        if (!string.IsNullOrWhiteSpace(record.Assignee) &&
                            !string.IsNullOrWhiteSpace(record.Container) &&
                            !string.IsNullOrWhiteSpace(record.Title))
                        {
                            string name = record.Title.Trim();
                            string container = record.Container.Trim();
                            string assignee = record.Assignee.Trim();
                            string type = record.IsContainer?.Trim() ?? "unknown";

                            var multiEntityData = new[]
                            {
                        new ChatData { Text = $"Find files in {container} by {assignee}", Label = $"{container} has files owned by {assignee}, including {name}." },
                        new ChatData { Text = $"Which items in {container} are assigned to {assignee}?", Label = $"{container} includes {name}, which is assigned to {assignee}." },
                        new ChatData { Text = $"Show me {type}s by {assignee} from {container}", Label = $"{name} is a {type} from {container} owned by {assignee}." }
                    };

                            foreach (var item in multiEntityData)
                            {
                                csv.WriteRecord(item);
                                csv.NextRecord();
                                trainingCount++;
                            }
                        }
                    }

                    // Fuzzy synonyms
                    var synonyms = new[] { "docs", "documents", "files", "entries", "items", "records" };
                    foreach (var word in synonyms)
                    {
                        var items = new[]
                        {
                    new ChatData { Text = $"How many {word} do we have?", Label = $"There are {records.Count} records in total." },
                    new ChatData { Text = $"Show {word} in HR", Label = $"Searching {word} in HR container." }
                };

                        foreach (var item in items)
                        {
                            csv.WriteRecord(item);
                            csv.NextRecord();
                            trainingCount++;
                        }
                    }

                    // Contextual queries
                    var contextItems = new[]
                    {
                new ChatData { Text = "What about yesterday's records?", Label = "Filtering by yesterday’s created date..." },
                new ChatData { Text = "Any recent files?", Label = "Here are the latest records based on creation date." },
                new ChatData { Text = "Remind me of the Finance container", Label = "Finance container has multiple items including..." },
                new ChatData { Text = "Who owns the last record?", Label = "The last record is assigned to ..." }
            };

                    foreach (var item in contextItems)
                    {
                        csv.WriteRecord(item);
                        csv.NextRecord();
                        trainingCount++;
                    }

                    // Advanced training generation
                    try
                    {
                        var advancetraning = new AdvancedTrainingDataGenerator();
                        var advancedData = new List<ChatData>();
                        advancedData.AddRange(advancetraning.GenerateContextualTraining(records));
                        advancedData.AddRange(advancetraning.GenerateComplexQueryTraining(records));
                        advancedData.AddRange(advancetraning.GenerateFuzzyMatchingTraining(records));
                        advancedData.AddRange(advancetraning.GenerateMultiEntityTraining(records));
                        advancedData.AddRange(advancetraning.GenerateTimeBasedTraining(records));
                        advancedData.AddRange(advancetraning.GenerateWorkflowTraining(records));
                        advancedData.AddRange(advancetraning.GenerateRangeQueries(records));

                        foreach (var item in advancedData)
                        {
                            csv.WriteRecord(item);
                            csv.NextRecord();
                            trainingCount++;
                        }

                        Console.WriteLine("Advanced training data generated and written.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error in advanced training generation: {ex.Message}");
                    }
                } // End of using stream/csv

                Console.WriteLine($"Total training records written: {trainingCount}");
                Console.WriteLine($"Appended advanced training data to: {filePath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fatal error in AppendAdvancedChatTrainingData: {ex.Message}");
            }
        }


        public bool AccessLog(string DatasetId, string WorkGroupURL)
        {
            if (_isConnected) return true;

            try
            {
                ConnectDataBase(DatasetId, WorkGroupURL);
                _isConnected = true;

                // ✅ Store session values
                _httpContextAccessor.HttpContext?.Session.SetString("DatasetId", DatasetId);
                _httpContextAccessor.HttpContext?.Session.SetString("WorkGroupUrl", WorkGroupURL);

                var ipAddress = _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString();
                var currentUserName = database.CurrentUser.Name;

                // ✅ Check if record already exists for this user, dataset, and workgroup
                bool exists = _dbContext.UserAccessLog.Any(x =>
                    x.UserName == currentUserName &&
                    x.DataSetId == DatasetId &&
                    x.WorkGroupServer == WorkGroupURL
                );

                if (exists)
                {
                    return true; //  User already logged
                }

                // ✅ Add only if not exists
                var useraccess = new UserAccessLog
                {
                    UserName = currentUserName,
                    DataSetId = DatasetId,
                    WorkGroupServer = WorkGroupURL,
                    CreatedDate = DateTime.Now,
                    IPAddress = ipAddress ?? "Unknown",
                    AppUniqueID = Guid.NewGuid().ToString(),
                };

                _dbContext.UserAccessLog.Add(useraccess);
                _dbContext.SaveChanges();

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            return false;


        }

        public bool CreateRecord(CreateRecord records)
        {
            try
            {
                var recordType = new RecordType(database, _settings.DefaultDocumnetType);
                var record = new Record(database, recordType);
                record.Title = records.Title;
                record.DateCreated = DateTime.Now;
                record.Author = database.CurrentUser;
                // Attach document to the record
                string filePath = records.AttachDocumentpath;
                record.SetDocument(filePath);
                record.Save();
                return true;

            }
            catch (Exception ex)
            {
                return false;
                throw;
            }
        }
    }
}
