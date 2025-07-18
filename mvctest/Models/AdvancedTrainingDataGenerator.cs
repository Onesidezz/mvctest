namespace mvctest.Models
{
    using Microsoft.ML.Data;
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using static mvctest.Models.ChatBot;

    public class AdvancedTrainingDataGenerator
    {
        private readonly Random _random = new Random();
        private readonly List<string> _synonyms = new() { "document", "file", "record", "item", "entry" };
        private readonly List<string> _questionStarters = new() { "What", "How", "Where", "When", "Who", "Which", "Can you tell me", "Show me", "Find", "Search for" };

        public void GenerateAdvancedChatTrainingData(List<RecordViewModel> records, string filePath)
        {
            var trainingData = new List<ChatData>();

            //trainingData.AddRange(GenerateLabelBasedTraining(records));
            trainingData.AddRange(GenerateContextualTraining(records));
            trainingData.AddRange(GenerateComplexQueryTraining(records));
            trainingData.AddRange(GenerateFuzzyMatchingTraining(records));
            trainingData.AddRange(GenerateMultiEntityTraining(records));
            trainingData.AddRange(GenerateTimeBasedTraining(records));
            trainingData.AddRange(GenerateWorkflowTraining(records));
            trainingData.AddRange(GenerateRangeQueries(records));


        }

        public List<ChatData> GenerateMultiEntityTraining(List<RecordViewModel> records)
        {
            var trainingData = new List<ChatData>();
            foreach (var record in records)
            {
                if (string.IsNullOrEmpty(record.Title) || string.IsNullOrEmpty(record.Assignee) || string.IsNullOrEmpty(record.Container))
                    continue;

                trainingData.Add(new ChatData
                {
                    Text = $"Find documents by {record.Assignee} in {record.Container} about {record.Title}",
                    Label = $"Matched record: {record.Title} created by {record.Assignee} in container {record.Container}.",
                    //Label = "MULTI_ENTITY",
                    Entities = ExtractEntities(record)
                });
            }

            return trainingData;
        }

        //public List<ChatData> GenerateLabelBasedTraining(List<RecordViewModel> records)
        //{
        //    var trainingData = new List<ChatData>();

        //    // Common intent types and their associated phrases
        //    var intents = new Dictionary<string, List<string>>
        //    {
        //        ["SearchDocument"] = new List<string> { "find", "search", "look for", "locate", "get me", "show me" },
        //        ["CountRecords"] = new List<string> { "how many", "count", "total", "number of" },
        //        ["GetInfo"] = new List<string> { "tell me about", "what is", "describe", "explain" },
        //        ["CompareRecords"] = new List<string> { "compare", "difference between", "which is better" },
        //        ["AnalyzeRecord"] = new List<string> { "analyze", "summary", "overview", "insights" }
        //    };

        //    foreach (var record in records)
        //    {
        //        foreach (var intent in intents)
        //        {
        //            foreach (var phrase in intent.Value)
        //            {
        //                var query = $"{phrase} {record.Title}";

        //                trainingData.Add(new ChatData
        //                {
        //                    Text = query,
        //                    Label = intent.Key,
        //                    Entities = ExtractEntities(record)
        //                });
        //            }
        //        }
        //    }

        //    return trainingData;
        //}


        public List<ChatData> GenerateComplexQueryTraining(List<RecordViewModel> records)
        {
            var trainingData = new List<ChatData>();

            // Multi-filter queries
            var dateGroups = records.GroupBy(r => r.DateCreated).Where(g => g.Count() > 1);
            foreach (var group in dateGroups)
            {
                var assignees = group.Select(r => r.Assignee).Distinct().ToList();
                if (assignees.Count > 1)
                {
                    trainingData.Add(new ChatData
                    {
                        Text = $"Show me all records from {group.Key} assigned to different people",
                        Label = $"On {group.Key}, there are records assigned to: {string.Join(", ", assignees)}. Total: {group.Count()} records."
                    });
                }
            }

            // Comparative queries
            var containerGroups = records.GroupBy(r => r.Container).Where(g => g.Count() > 1);
            foreach (var group in containerGroups)
            {
                var types = group.Select(r => r.IsContainer).Distinct().ToList();
                trainingData.Add(new ChatData
                {
                    Text = $"What types of items are in {group.Key}?",
                    Label = $"Container {group.Key} contains {group.Count()} items of types: {string.Join(", ", types)}."
                });
            }

            // Range queries
            trainingData.AddRange(GenerateRangeQueries(records));

            return trainingData;
        }

        public List<ChatData> GenerateFuzzyMatchingTraining(List<RecordViewModel> records)
        {
            var trainingData = new List<ChatData>();

            foreach (var record in records) 
            {
                if (string.IsNullOrEmpty(record.Title)) continue;

                // Generate typos and variations
                var variations = GenerateTypoVariations(record.Title);
                foreach (var variation in variations)
                {
                    trainingData.Add(new ChatData
                    {
                        Text = $"Tell me about {variation}",
                        Label = $"I think you meant '{record.Title}'. {record.Title} is a {record.IsContainer} created on {record.DateCreated} by {record.Assignee}."
                    });
                }

                // Partial matches
                var words = record.Title.Split(' ');
                if (words.Length > 1)
                {
                    var partialQuery = string.Join(" ", words.Take(Math.Max(1, words.Length - 1)));
                    trainingData.Add(new ChatData
                    {
                        Text = $"Find documents with {partialQuery}",
                        Label = $"Found: {record.Title} (matches '{partialQuery}') - {record.IsContainer} created on {record.DateCreated}."
                    });
                }
            }

            return trainingData;
        }

        public List<ChatData> GenerateContextualTraining(List<RecordViewModel> records)
        {
            var trainingData = new List<ChatData>();

            // Follow-up questions
            trainingData.AddRange(new[]
            {
            new ChatData { Text = "tell me more", Label = "Please specify which record you'd like more information about." },
            new ChatData { Text = "show me similar", Label = "I can show you similar records. Which record would you like me to find similar items for?" },
            new ChatData { Text = "what else", Label = "I can help you with record searches, counts, summaries, and detailed information. What would you like to know?" },
            new ChatData { Text = "previous", Label = "I can help you find previous versions or related records. Which document are you referring to?" }
        });

            // Conversational context
            foreach (var record in records)
            {
                trainingData.Add(new ChatData
                {
                    Text = "and who created this?",
                    Label = $"If you're asking about {record.Title}, it was created by {record.Assignee} on {record.DateCreated}."
                });
            }

            return trainingData;
        }

        public List<ChatData> GenerateTimeBasedTraining(List<RecordViewModel> records)
        {
            var trainingData = new List<ChatData>();

            // Recent/old document queries
            trainingData.AddRange(new[]
            {
            new ChatData { Text = "show me recent documents", Label = GenerateRecentDocumentsResponse(records) },
            new ChatData { Text = "what was created last week", Label = GenerateTimeRangeResponse(records, "last week") },
            new ChatData { Text = "old documents", Label = GenerateOldDocumentsResponse(records) },
            new ChatData { Text = "documents from this month", Label = GenerateTimeRangeResponse(records, "this month") }
        });

            // Specific time periods
            var dateGroups = records.GroupBy(r => r.DateCreated?.Substring(0, 7)) // Year-Month
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .OrderByDescending(g => g.Key);

            foreach (var group in dateGroups)
            {
                trainingData.Add(new ChatData
                {
                    Text = $"what happened in {group.Key}?",
                    Label = $"In {group.Key}, {group.Count()} records were created by {group.Select(r => r.Assignee).Distinct().Count()} different users."
                });
            }

            return trainingData;
        }

        public List<ChatData> GenerateWorkflowTraining(List<RecordViewModel> records)
        {
            var trainingData = new List<ChatData>();

            // Workflow-related queries
            trainingData.AddRange(new[]
            {
            new ChatData { Text = "what should I do next?", Label = "I can help you find pending tasks, recent documents, or suggest actions based on your records." },
            new ChatData { Text = "my tasks", Label = GenerateUserTasksResponse(records) },
            new ChatData { Text = "what needs attention?", Label = GenerateAttentionResponse(records) },
            new ChatData { Text = "summary of my work", Label = GenerateWorkSummaryResponse(records) }
        });

            // Action-oriented queries
            foreach (var assignee in records.Select(r => r.Assignee).Distinct())
            {
                if (string.IsNullOrEmpty(assignee)) continue;

                var userRecords = records.Where(r => r.Assignee == assignee).ToList();
                trainingData.Add(new ChatData
                {
                    Text = $"what is {assignee} working on?",
                    Label = $"{assignee} has {userRecords.Count} records assigned. Recent activity includes {userRecords.Select(r => r.Title).Aggregate((a, b) => a + ", " + b)}."
                });
            }

            return trainingData;
        }

        public List<string> GenerateTypoVariations(string text)
        {
            var variations = new List<string>();
            if (text.Length < 3) return variations;

            // Common typos
            variations.Add(text.Replace("a", "e")); // Simple substitution
            variations.Add(text.Replace("i", "y")); // Common vowel swap

            // Missing characters
            if (text.Length > 3)
            {
                variations.Add(text.Substring(1)); // Missing first char
                variations.Add(text.Substring(0, text.Length - 1)); // Missing last char
            }

            // Extra characters
            variations.Add(text + "s"); // Common plural
            variations.Add(text.Replace(" ", "")); // No spaces

            return variations.Where(v => v != text && !string.IsNullOrEmpty(v)).ToList();
        }

        public string GenerateContextualResponse(string intent, RecordViewModel record)
        {
            return intent switch
            {
                "SEARCH" => $"Found: {record.Title} - {record.IsContainer} in {record.Container}, created {record.DateCreated}",
                "COUNT" => $"There is 1 record matching '{record.Title}' in container {record.Container}",
                "INFO" => $"{record.Title} is a {record.IsContainer} created on {record.DateCreated} by {record.Assignee} in container {record.Container}",
                "ANALYZE" => $"Analysis of {record.Title}: Type={record.IsContainer}, Owner={record.Assignee}, Location={record.Container}, Created={record.DateCreated}",
                _ => $"Information about {record.Title}: {record.IsContainer} created by {record.Assignee}"
            };
        }

        public string ExtractEntities(RecordViewModel record)
        {
            var entities = new List<string>();
            if (!string.IsNullOrEmpty(record.Title)) entities.Add($"TITLE:{record.Title}");
            if (!string.IsNullOrEmpty(record.Assignee)) entities.Add($"PERSON:{record.Assignee}");
            if (!string.IsNullOrEmpty(record.Container)) entities.Add($"CONTAINER:{record.Container}");
            if (!string.IsNullOrEmpty(record.DateCreated)) entities.Add($"DATE:{record.DateCreated}");

            return string.Join("|", entities);
        }

        //private void WriteEnhancedCsv(List<ChatData> trainingData, string filePath)
        //{
        //    using (var writer = new StreamWriter(filePath, false, Encoding.UTF8))
        //    using (var csv = new CsvHelper.CsvWriter(writer, CultureInfo.InvariantCulture))
        //    {
        //        csv.WriteHeader<EnhancedChatData>();
        //        csv.NextRecord();

        //        foreach (var item in trainingData)
        //        {
        //            csv.WriteRecord(new EnhancedChatData
        //            {
        //                Text = item.Text,
        //                Label = item.Label,
        //                Label = item.Label ?? "GENERAL",
        //                Entities = item.Entities ?? "",
        //                Confidence = CalculateConfidence(item),
        //                Context = ExtractContext(item)
        //            });
        //            csv.NextRecord();
        //        }
        //    }
        //}

        // Helper methods for response generation
        private string GenerateRecentDocumentsResponse(List<RecordViewModel> records)
        {
            var recentDocs = records.OrderByDescending(r => r.DateCreated);
            return $"Recent documents: {string.Join(", ", recentDocs.Select(r => r.Title))}";
        }

        private string GenerateWorkSummaryResponse(List<RecordViewModel> records)
        {
            var totalCount = records.Count;
            var uniqueContainers = records.Select(r => r.Container).Distinct().Count();
            var uniqueAssignees = records.Select(r => r.Assignee).Distinct().Count();

            return $"Summary: {totalCount} total records across {uniqueContainers} containers, managed by {uniqueAssignees} people.";
        }

        private double CalculateConfidence(ChatData item)
        {
            // Simple confidence calculation based on text complexity
            var wordCount = item.Text.Split(' ').Length;
            return Math.Min(1.0, 0.5 + (wordCount * 0.1));
        }

        private string ExtractContext(ChatData item)
        {
            // Extract context clues from the text
            var contextWords = new[] { "recent", "old", "last", "this", "previous", "next", "similar" };
            var words = item.Text.ToLower().Split(' ');
            var contexts = words.Where(w => contextWords.Contains(w)).ToList();

            return string.Join(",", contexts);
        }

        // Additional helper methods for other response types...
        private string GenerateTimeRangeResponse(List<RecordViewModel> records, string timeRange)
        {
            // Implementation for time range queries
            return $"Time range query for {timeRange} - showing relevant records from the specified period.";
        }

        private string GenerateOldDocumentsResponse(List<RecordViewModel> records)
        {
            var oldDocs = records.OrderBy(r => r.DateCreated);
            return $"Oldest documents: {string.Join(", ", oldDocs.Select(r => r.Title))}";
        }

        private string GenerateUserTasksResponse(List<RecordViewModel> records)
        {
            var currentUser = records.FirstOrDefault()?.Assignee ?? "Unknown";
            var userRecords = records.Where(r => r.Assignee == currentUser);
            return $"Your tasks: {string.Join(", ", userRecords.Select(r => r.Title))}";
        }

        private string GenerateAttentionResponse(List<RecordViewModel> records)
        {
            // Logic to identify items needing attention
            return "Items that may need attention: Recent uploads, pending reviews, or frequently accessed documents.";
        }

        public List<ChatData> GenerateRangeQueries(List<RecordViewModel> records)
        {
            var trainingData = new List<ChatData>();

            var dateRange = records
                .Where(r => !string.IsNullOrEmpty(r.DateCreated))
                .OrderBy(r => r.DateCreated)
                .ToList();

            if (dateRange.Count >= 2)
            {
                var start = dateRange.First().DateCreated;
                var end = dateRange.Last().DateCreated;

                trainingData.Add(new ChatData
                {
                    Text = $"Show me records between {start} and {end}",
                    Label = $"There are {dateRange.Count} records between {start} and {end}.",
                    //Label = "RANGE_QUERY",
                    Entities = $"DATE_START:{start}|DATE_END:{end}"
                });
            }

            return trainingData;
        }
    }

}
