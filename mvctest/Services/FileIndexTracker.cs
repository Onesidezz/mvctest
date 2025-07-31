using System.Text.Json;

namespace mvctest.Services
{
    public class FileIndexTracker
    {
        private readonly HashSet<string> indexedFiles;
        private readonly Dictionary<string, DateTime> fileLastModified;
        private readonly string trackingFilePath;

        public FileIndexTracker(string indexDirectory)
        {
            trackingFilePath = Path.Combine(indexDirectory, "indexed_files.json");
            indexedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            fileLastModified = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            LoadTrackingData();
        }

        private void LoadTrackingData()
        {
            try
            {
                if (File.Exists(trackingFilePath))
                {
                    var json = File.ReadAllText(trackingFilePath);
                    var data = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(json);

                    if (data != null)
                    {
                        foreach (var kvp in data)
                        {
                            indexedFiles.Add(kvp.Key);
                            fileLastModified[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not load tracking data: {ex.Message}");
            }
        }

        public void SaveTrackingData()
        {
            try
            {
                var json = JsonSerializer.Serialize(fileLastModified, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(trackingFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not save tracking data: {ex.Message}");
            }
        }

        public bool ShouldIndexFile(string filePath)
        {
            if (!File.Exists(filePath))
                return false;

            var lastWriteTime = File.GetLastWriteTime(filePath);

            if (!indexedFiles.Contains(filePath))
                return true;

            if (fileLastModified.TryGetValue(filePath, out var lastIndexed))
            {
                return lastWriteTime > lastIndexed;
            }

            return true;
        }

        public void MarkFileAsIndexed(string filePath)
        {
            var lastWriteTime = File.GetLastWriteTime(filePath);
            indexedFiles.Add(filePath);
            fileLastModified[filePath] = lastWriteTime;
        }

        public void RemoveDeletedFiles()
        {
            var filesToRemove = new List<string>();

            foreach (var filePath in indexedFiles)
            {
                if (!File.Exists(filePath))
                {
                    filesToRemove.Add(filePath);
                }
            }

            foreach (var fileToRemove in filesToRemove)
            {
                indexedFiles.Remove(fileToRemove);
                fileLastModified.Remove(fileToRemove);
            }

            if (filesToRemove.Count > 0)
            {
                Console.WriteLine($"Removed {filesToRemove.Count} deleted file(s) from tracking.");
            }
        }
      

        public int GetIndexedFileCount() => indexedFiles.Count;



    }


}
