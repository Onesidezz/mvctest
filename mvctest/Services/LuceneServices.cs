using iText.Commons.Actions.Contexts;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.Extensions.Options;
using Microsoft.VisualBasic.FileIO;
using mvctest.Models;
using Directory = System.IO.Directory;
using SearchOption = System.IO.SearchOption;

namespace mvctest.Services
{
    public class LuceneServices : ILuceneInterface
    {
        private readonly LuceneVersion LuceneVersion = LuceneVersion.LUCENE_48;
        private readonly string IndexPath;
        private IndexWriter indexWriter;
        private StandardAnalyzer analyzer;
        private readonly AppSettings _settings;
        private DirectoryReader indexReader;
        private IndexSearcher indexSearcher;

        public LuceneServices(IOptions<AppSettings> settings)
        {
            _settings = settings.Value;

            // Use the IndexDirectory from settings, fallback to default if not set
            IndexPath = !string.IsNullOrEmpty(_settings.IndexDirectory)
                ? _settings.IndexDirectory
                : Path.Combine(Environment.CurrentDirectory, "index");

            InitializeLucene();

            // Only auto-index if FolderDirectory is specified
            if (!string.IsNullOrEmpty(_settings.FolderDirectory))
            {
                BatchIndexFilesFromDirectory(_settings.FolderDirectory);
            }

        }

        public void InitializeLucene()
        {
            try
            {
                if (!Directory.Exists(IndexPath))
                {
                    Directory.CreateDirectory(IndexPath);
                    Console.WriteLine($"Created index directory: {IndexPath}");
                }

                var indexDir = FSDirectory.Open(IndexPath);
                analyzer = new StandardAnalyzer(LuceneVersion);

                var config = new IndexWriterConfig(LuceneVersion, analyzer)
                {
                    OpenMode = OpenMode.CREATE_OR_APPEND
                };

                indexWriter = new IndexWriter(indexDir, config);
                Console.WriteLine($"Lucene.NET initialized successfully at: {IndexPath}");

                // DON'T create reader and searcher here - they will become stale
                // Create them fresh in each search operation instead
                indexReader = null;
                indexSearcher = null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing Lucene: {ex.Message}");
                throw;
            }
        }


        public void BatchIndexFilesFromDirectory(string directoryPath)
        {
            var allFiles = new List<string>();
            var supportedPatterns = new[] { "*.txt", "*.pdf", "*.docx", "*.xlsx", "*.pptx" };

            if (!Directory.Exists(directoryPath))
            {
                Console.WriteLine($"Directory does not exist: {directoryPath}");
                return;
            }
        
            // Get all supported file types

            foreach (var pattern in supportedPatterns)
            {
                allFiles.AddRange(Directory.GetFiles(directoryPath, pattern, SearchOption.AllDirectories));
            }

            Console.WriteLine($"Found {allFiles.Count} supported files to index.");

            if (allFiles.Count == 0)
            {
                Console.WriteLine("No supported files found in directory.");
                return;
            }
         
            // Use the enhanced IndexFiles method for batch processing
            IndexFilesInternal(allFiles, forceReindex: false);
        }
        public void BatchIndexFilesFromContentManager(List<string> directories)
        {
            var supportedPatterns = new[] { "*.txt", "*.pdf", "*.docx", "*.xlsx", "*.pptx" };

            // Validate directories
            foreach (var dir in directories)
            {
                if (!Directory.Exists(dir))
                {
                    Console.WriteLine($"Directory does not exist: {dir}");
                    return; // Exit if any directory is invalid
                }
            }

            var allSupportedFiles = new List<string>();

            // Get all supported files from each directory
            foreach (var dir in directories)
            {
                foreach (var pattern in supportedPatterns)
                {
                    var files = Directory.GetFiles(dir, pattern, SearchOption.AllDirectories);
                    allSupportedFiles.AddRange(files);
                }
            }

            Console.WriteLine($"Found {allSupportedFiles.Count} supported files to index.");

            if (allSupportedFiles.Count == 0)
            {
                Console.WriteLine("No supported files found in the provided directories.");
                return;
            }

            // Use the enhanced IndexFiles method for batch processing
            IndexFilesInternal(allSupportedFiles, forceReindex: false);
        }


        public void IndexFilesInternal(List<string> filesToIndex, bool forceReindex)
        {
            if (filesToIndex.Count == 0)
            {
                Console.WriteLine("No supported files found.");
                Console.WriteLine("Supported formats: .txt, .pdf, .docx, .xlsx, .pptx");
                return;
            }

            // Initialize file tracking
            var tracker = new FileIndexTracker(IndexPath);
            tracker.RemoveDeletedFiles();

            // Filter files based on indexing status
            var filesToProcess = new List<string>();
            var skippedFiles = 0;
            var duplicateFiles = 0;

            foreach (var filePath in filesToIndex)
            {
                // Check if file is already in index using Lucene search
                if (!forceReindex && IsFileAlreadyInIndex(filePath))
                {
                    duplicateFiles++;
                    Console.WriteLine($"Skipping duplicate: {Path.GetFileName(filePath)}");
                    continue;
                }

                if (forceReindex || tracker.ShouldIndexFile(filePath))
                {
                    filesToProcess.Add(filePath);
                }
                else
                {
                    skippedFiles++;
                }
            }

            Console.WriteLine($"Found {filesToIndex.Count} supported file(s).");
            Console.WriteLine($"Skipping {duplicateFiles} duplicate file(s).");
            Console.WriteLine($"Skipping {skippedFiles} already indexed file(s).");
            Console.WriteLine($"Processing {filesToProcess.Count} file(s)...");

            if (filesToProcess.Count == 0)
            {
                Console.WriteLine("All files are already up to date in the index.");
                return;
            }

            int indexed = 0;
            int failed = 0;
            int updated = 0;

            foreach (var filePath in filesToProcess)
            {
                try
                {
                    // Always delete any existing documents for this file to prevent duplicates
                    DeleteExistingDocument(filePath);

                    // Check if this was an update
                    bool isUpdate = IsFileIndexed(filePath);

                    var content = FileTextExtractor.ExtractTextFromFile(filePath);
                    var fileName = Path.GetFileName(filePath);
                    var fileExtension = Path.GetExtension(filePath).ToLower().TrimStart('.');

                    var doc = new Document();
                    doc.Add(new TextField("filename", fileName, Field.Store.YES));
                    doc.Add(new TextField("filepath", filePath, Field.Store.YES));
                    doc.Add(new TextField("content", content, Field.Store.YES));
                    doc.Add(new StringField("filetype", fileExtension, Field.Store.YES));
                    doc.Add(new StringField("indexed_date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Field.Store.YES));
                    doc.Add(new StringField("file_modified_date", File.GetLastWriteTime(filePath).ToString("yyyy-MM-dd HH:mm:ss"), Field.Store.YES));

                    indexWriter.AddDocument(doc);
                    tracker.MarkFileAsIndexed(filePath);
                    indexed++;

                    if (isUpdate)
                    {
                        updated++;
                    }

                    string status = isUpdate ? "Updated" : "Indexed";
                    Console.WriteLine($"{status}: {fileName} ({fileExtension.ToUpper()})");
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.WriteLine($"Error indexing {Path.GetFileName(filePath)}: {ex.Message}");
                }
            }

            try
            {
                indexWriter.Commit();

                // Force merge to remove deleted documents
                indexWriter.ForceMerge(1);

                tracker.SaveTrackingData();

                Console.WriteLine($"\nIndexing completed:");
                Console.WriteLine($"  Successfully indexed: {indexed} file(s)");
                if (updated > 0)
                {
                    Console.WriteLine($"  Updated existing: {updated} file(s)");
                }
                if (failed > 0)
                {
                    Console.WriteLine($"  Failed to index: {failed} file(s)");
                }
                if (skippedFiles > 0)
                {
                    Console.WriteLine($"  Skipped (already indexed): {skippedFiles} file(s)");
                }
                if (duplicateFiles > 0)
                {
                    Console.WriteLine($"  Skipped (duplicates): {duplicateFiles} file(s)");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error committing index: {ex.Message}");
            }
        }
        private bool IsFileAlreadyInIndex(string filePath)
        {
            try
            {
                // Commit any pending changes first
                indexWriter.Commit();

                using var reader = DirectoryReader.Open(indexWriter.Directory);
                var searcher = new IndexSearcher(reader);

                // Search for exact filepath match
                var query = new TermQuery(new Term("filepath", filePath));
                var hits = searcher.Search(query, 1);

                return hits.TotalHits > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking if file is indexed: {ex.Message}");
                return false;
            }
        }

        private void DeleteExistingDocument(string filePath)
        {
            try
            {
                // Delete by exact filepath
                var exactPathQuery = new TermQuery(new Term("filepath", filePath));
                indexWriter.DeleteDocuments(exactPathQuery);

                // Also delete by filename as a safety measure
                var fileName = Path.GetFileName(filePath);
                var fileNameQuery = new TermQuery(new Term("filename", fileName));
                indexWriter.DeleteDocuments(fileNameQuery);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting existing document: {ex.Message}");
            }
        }
        public List<SearchResultModel> SearchFiles(string query)
        {
            var resultList = new List<SearchResultModel>();
            if (string.IsNullOrWhiteSpace(query))
            {
                Console.WriteLine("Please enter a valid search query.");
                return resultList;
            }

            try
            {
                // First, check if index exists and has content
                var indexDirectory = FSDirectory.Open(IndexPath);

                if (!DirectoryReader.IndexExists(indexDirectory))
                {
                    Console.WriteLine("Index does not exist. Please index some files first.");
                    return resultList;
                }

                // Commit any pending changes to ensure index is up to date
                indexWriter.Commit();

                // ALWAYS create a fresh reader for search to see latest changes
                using var reader = DirectoryReader.Open(indexDirectory);

                // Check if index has any documents
                if (reader.NumDocs == 0)
                {
                    Console.WriteLine("Index is empty. Please index some files first.");
                    return resultList;
                }

                var searcher = new IndexSearcher(reader);
                var parser = new MultiFieldQueryParser(LuceneVersion, new[] { "filename", "content" }, analyzer);
                var luceneQuery = parser.Parse(query);

                // Use a higher number to get more results initially, then we can deduplicate
                var hits = searcher.Search(luceneQuery, 100).ScoreDocs;

                if (hits.Length == 0)
                {
                    Console.WriteLine("No results found.");
                    return resultList;
                }

                // Use a HashSet to track unique files and prevent duplicates
                var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                Console.WriteLine($"\nFound {hits.Length} result(s):\n");
                Console.WriteLine(new string('=', 80));

                int resultCount = 0;
                for (int i = 0; i < hits.Length && resultCount < 20; i++)
                {
                    var doc = searcher.Doc(hits[i].Doc);
                    var fileName = doc.Get("filename");
                    var filePath = doc.Get("filepath");
                    var content = doc.Get("content");
                    var date = doc.Get("indexed_date"); 
                    var score = hits[i].Score;

                    // Skip if we've already seen this file (prevent duplicates)
                    if (!seenFiles.Add(filePath))
                    {
                        Console.WriteLine($"Skipping duplicate result for: {fileName}");
                        continue;
                    }

                    var snippets = GetAllContentSnippets(content, query, 250);

                    var resultModel = new SearchResultModel
                    {
                        FileName = fileName,
                        FilePath = filePath,
                        Score = score,
                        Snippets = snippets,
                        date = date,    
                    };

                    resultList.Add(resultModel);
                    resultCount++;

                    Console.WriteLine($"Result {resultCount}: {fileName}");
                    Console.WriteLine($"Path: {filePath}");
                    Console.WriteLine($"Score: {score:F2}");
                    Console.WriteLine($"Occurrences: {snippets.Count}");
                    Console.WriteLine(new string('-', 80));
                }

                return resultList;
            }
            catch (IndexNotFoundException ex)
            {
                Console.WriteLine("Index not found. Please index some files first.");
                Console.WriteLine($"Index path: {IndexPath}");
                return resultList;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during search: {ex.Message}");

                // Additional debugging information
                if (ex.Message.Contains("no segments"))
                {
                    Console.WriteLine("The index appears to be empty or corrupted.");
                    Console.WriteLine("Try re-indexing your files or clearing and rebuilding the index.");

                    // Check what files exist in the index directory
                    if (Directory.Exists(IndexPath))
                    {
                        var files = Directory.GetFiles(IndexPath);
                        Console.WriteLine($"Files in index directory: {string.Join(", ", files.Select(Path.GetFileName))}");
                    }
                }

                return resultList;
            }
        }
        public bool IsFileIndexed(string filePath)
        {
            try
            {
                // Commit pending changes first
                indexWriter.Commit();

                // Always use a fresh reader
                using var reader = DirectoryReader.Open(indexWriter.Directory);
                var searcher = new IndexSearcher(reader);

                var query = new TermQuery(new Term("filepath", filePath));
                var hits = searcher.Search(query, 1);

                return hits.TotalHits > 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error checking if file is indexed: {ex.Message}");
                return false;
            }
        }
        public List<string> GetAllContentSnippets(string content, string query, int maxLength)
        {
            var snippets = new List<string>();

            if (string.IsNullOrWhiteSpace(content))
            {
                snippets.Add("No content available.");
                return snippets;
            }

            var queryWords = query.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var contentLower = content.ToLower();
            var foundPositions = new List<int>();

            foreach (var word in queryWords)
            {
                int index = 0;
                while ((index = contentLower.IndexOf(word, index)) != -1)
                {
                    foundPositions.Add(index);
                    index += word.Length;
                }
            }

            if (foundPositions.Count == 0)
            {
                var fallback = content.Length <= maxLength ? content : content.Substring(0, maxLength) + "...";
                snippets.Add(fallback);
                return snippets;
            }

            foundPositions.Sort();
            var filteredPositions = new List<int>();

            foreach (var pos in foundPositions)
            {
                bool tooClose = false;
                foreach (var existing in filteredPositions)
                {
                    if (Math.Abs(pos - existing) < maxLength / 2)
                    {
                        tooClose = true;
                        break;
                    }
                }
                if (!tooClose)
                {
                    filteredPositions.Add(pos);
                }
            }

            foreach (var position in filteredPositions)
            {
                int start = position;
                while (start > 0 && content[start] != '.' && start > position - maxLength)
                    start--;

                int end = position;
                while (end < content.Length && content[end] != '.' && end < position + maxLength)
                    end++;

                start = Math.Max(0, start);
                end = Math.Min(content.Length - 1, end);

                var snippet = content.Substring(start, end - start + 1).Trim();

                if (snippet.Length > maxLength)
                {
                    snippet = snippet.Substring(0, maxLength) + "...";
                }

                if (start > 0) snippet = "..." + snippet;
                if (end < content.Length - 1) snippet += "...";

                foreach (var word in queryWords)
                {
                    var regex = new System.Text.RegularExpressions.Regex($@"\b{System.Text.RegularExpressions.Regex.Escape(word)}\b",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    snippet = regex.Replace(snippet, $"**{word.ToUpper()}**");
                }

                snippets.Add(snippet);
            }

            return snippets;
        }
       
        public string GetContentSnippet(string content, string query, int maxLength)
        {
            var snippets = GetAllContentSnippets(content, query, maxLength);
            return snippets.FirstOrDefault() ?? "No content available.";
        }

        public void ShowIndexStats()
        {
            try
            {
                indexWriter.Commit();
                using var reader = DirectoryReader.Open(indexWriter.Directory);

                Console.WriteLine("=== Index Statistics ===");
                Console.WriteLine($"Total indexed documents: {reader.NumDocs}");
                Console.WriteLine($"Total deleted documents: {reader.NumDeletedDocs}");
                Console.WriteLine($"Index directory: {IndexPath}");

                if (reader.NumDocs > 0)
                {
                    Console.WriteLine("\nSample documents:");
                    var searcher = new IndexSearcher(reader);
                    var allDocs = searcher.Search(new MatchAllDocsQuery(), 5);

                    foreach (var hit in allDocs.ScoreDocs)
                    {
                        var doc = searcher.Doc(hit.Doc);
                        Console.WriteLine($"- {doc.Get("filename")} ({doc.Get("filepath")})");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving index statistics: {ex.Message}");
            }
        }

        public void ShowIndexingStats()
        {
            var tracker = new FileIndexTracker(IndexPath);
            Console.WriteLine($"Index Statistics:");
            Console.WriteLine($"  Location: {IndexPath}");
            Console.WriteLine($"  Total tracked files: {tracker.GetIndexedFileCount()}");

            try
            {
                using var reader = DirectoryReader.Open(FSDirectory.Open(IndexPath));
                Console.WriteLine($"  Documents in index: {reader.NumDocs}");
                Console.WriteLine($"  Index size: {GetDirectorySize(IndexPath):N0} bytes");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Could not read index stats: {ex.Message}");
            }
        }

        private long GetDirectorySize(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
                return 0;

            return Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories)
                           .Sum(file => new FileInfo(file).Length);
        }

      

        public void ClearIndex(string confirmation = null)
        {
            if (confirmation?.ToLower() != "y")
            {
                Console.WriteLine("Index clearing cancelled.");
                return;
            }

            try
            {
                indexWriter.DeleteAll();
                indexWriter.Commit();

                // Also clear the tracking file
                var tracker = new FileIndexTracker(IndexPath);
                var trackingFile = Path.Combine(IndexPath, "indexed_files.json");
                if (File.Exists(trackingFile))
                {
                    File.Delete(trackingFile);
                }

                Console.WriteLine("Index and tracking data cleared successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error clearing index: {ex.Message}");
            }
        }

        public void CleanupLucene()
        {
            try
            {
                indexWriter?.Dispose();
                analyzer?.Dispose();
                Console.WriteLine("Lucene.NET resources cleaned up.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during cleanup: {ex.Message}");
            }
        }

        // IDisposable implementation
        public void Dispose()
        {
            CleanupLucene();
        }
       

    }
}

