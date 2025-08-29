using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.QueryParsers.Classic;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.Extensions.Options;
using mvctest.Models;
using System.Text;
using Directory = System.IO.Directory;
using SearchOption = System.IO.SearchOption;

namespace mvctest.Services
{
    public class LuceneServices : ILuceneInterface
    {
        private readonly LuceneVersion LuceneVersion = LuceneVersion.LUCENE_48;
        private readonly string IndexPath;
        private readonly string IndexPath2;

        private IndexWriter indexWriter;
        private IndexWriter indexWriter2;
        private StandardAnalyzer analyzer;
        private StandardAnalyzer analyzer2;
        private readonly AppSettings _settings;
        private DirectoryReader indexReader;
        private IndexSearcher indexSearcher;
        private readonly IServiceProvider _serviceProvider;
        private readonly TextEmbeddingService _embeddingService;


        public LuceneServices(IOptions<AppSettings> settings, IServiceProvider serviceProvider)
        {
            _settings = settings.Value;

            // Use the IndexDirectory from settings, fallback to default if not set
            IndexPath = !string.IsNullOrEmpty(_settings.IndexDirectory)
                ? _settings.IndexDirectory
                : Path.Combine(Environment.CurrentDirectory, "index");
            
            // Use IndexDirectory2 from settings, fallback to default if not set
            IndexPath2 = !string.IsNullOrEmpty(_settings.IndexDirectory2)
                ? _settings.IndexDirectory2
                : Path.Combine(Environment.CurrentDirectory, "index2");
            
            InitializeLucene();
            InitializeLucene2();
            _serviceProvider = serviceProvider;

            // Initialize embedding service for semantic search
            var modelPath = _settings.EmbeddingModelPath ?? Path.Combine(Environment.CurrentDirectory, "models", "sentence-transformer.onnx");
            try
            {
                _embeddingService = new TextEmbeddingService(modelPath);
                Console.WriteLine("Text embedding service initialized successfully.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Could not load embedding model: {ex.Message}");
                _embeddingService = null;
            }

            //// Only auto-index if FolderDirectory is specified
            //if (!string.IsNullOrEmpty(_settings.FolderDirectory))
            //{
            //    BatchIndexFilesFromDirectory(_settings.FolderDirectory);
            //}

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
                
                // Check for stale locks and clear them
                var lockFile = Path.Combine(IndexPath, "write.lock");
                if (File.Exists(lockFile))
                {
                    Console.WriteLine("Found stale lock file for IndexPath, attempting to clear it...");
                    try
                    {
                        File.Delete(lockFile);
                        Console.WriteLine("Deleted stale lock file for IndexPath");
                    }
                    catch (Exception lockEx)
                    {
                        Console.WriteLine($"Could not delete lock file: {lockEx.Message}");
                    }
                }

                analyzer = new StandardAnalyzer(LuceneVersion);

                var config = new IndexWriterConfig(LuceneVersion, analyzer)
                {
                    OpenMode = OpenMode.CREATE_OR_APPEND,
                    WriteLockTimeout = 10000 // 10 second timeout
                };

                indexWriter = new IndexWriter(indexDir, config);
                Console.WriteLine($"Lucene.NET initialized successfully at: {IndexPath}");

                // DON'T create reader and searcher here - they will become stale
                // Create them fresh in each search operation instead
                indexReader = null;
                indexSearcher = null;
            }
            catch (LockObtainFailedException ex)
            {
                Console.WriteLine($"Lock obtain failed for IndexPath: {ex.Message}");
                Console.WriteLine("Attempting to force unlock IndexPath...");
                
                try
                {
                    // Force unlock by deleting lock file
                    var lockFile = Path.Combine(IndexPath, "write.lock");
                    if (File.Exists(lockFile))
                    {
                        File.Delete(lockFile);
                        Console.WriteLine("Deleted lock file for IndexPath");
                        
                        // Retry initialization
                        var indexDir = FSDirectory.Open(IndexPath);
                        analyzer = new StandardAnalyzer(LuceneVersion);
                        var config = new IndexWriterConfig(LuceneVersion, analyzer)
                        {
                            OpenMode = OpenMode.CREATE_OR_APPEND,
                            WriteLockTimeout = 5000
                        };
                        indexWriter = new IndexWriter(indexDir, config);
                        Console.WriteLine($"Lucene.NET initialized successfully after lock cleanup at: {IndexPath}");
                        
                        indexReader = null;
                        indexSearcher = null;
                    }
                    else
                    {
                        throw; // Re-throw if no lock file found
                    }
                }
                catch (Exception retryEx)
                {
                    Console.WriteLine($"Failed to initialize IndexPath after retry: {retryEx.Message}");
                    throw;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing Lucene: {ex.Message}");
                throw;
            }
        }

        public void InitializeLucene2()
        {
            try
            {
                if (!Directory.Exists(IndexPath2))
                {
                    Directory.CreateDirectory(IndexPath2);
                    Console.WriteLine($"Created index2 directory: {IndexPath2}");
                }

                var indexDir2 = FSDirectory.Open(IndexPath2);
                
                // Check for stale locks and clear them
                var lockFile = Path.Combine(IndexPath2, "write.lock");
                if (File.Exists(lockFile))
                {
                    Console.WriteLine("Found stale lock file for IndexPath2, attempting to clear it...");
                    try
                    {
                        File.Delete(lockFile);
                        Console.WriteLine("Deleted stale lock file for IndexPath2");
                    }
                    catch (Exception lockEx)
                    {
                        Console.WriteLine($"Could not delete lock file: {lockEx.Message}");
                    }
                }

                analyzer2 = new StandardAnalyzer(LuceneVersion);

                var config2 = new IndexWriterConfig(LuceneVersion, analyzer2)
                {
                    OpenMode = OpenMode.CREATE_OR_APPEND,
                    WriteLockTimeout = 10000 // 10 second timeout
                };

                indexWriter2 = new IndexWriter(indexDir2, config2);
                Console.WriteLine($"Lucene.NET IndexPath2 initialized successfully at: {IndexPath2}");
            }
            catch (LockObtainFailedException ex)
            {
                Console.WriteLine($"Lock obtain failed for IndexPath2: {ex.Message}");
                Console.WriteLine("Attempting to force unlock IndexPath2...");
                
                try
                {
                    // Force unlock by deleting lock file
                    var lockFile = Path.Combine(IndexPath2, "write.lock");
                    if (File.Exists(lockFile))
                    {
                        File.Delete(lockFile);
                        Console.WriteLine("Deleted lock file for IndexPath2");
                        
                        // Retry initialization
                        var indexDir2 = FSDirectory.Open(IndexPath2);
                        analyzer2 = new StandardAnalyzer(LuceneVersion);
                        var config2 = new IndexWriterConfig(LuceneVersion, analyzer2)
                        {
                            OpenMode = OpenMode.CREATE_OR_APPEND,
                            WriteLockTimeout = 5000
                        };
                        indexWriter2 = new IndexWriter(indexDir2, config2);
                        Console.WriteLine($"Lucene.NET IndexPath2 initialized successfully after lock cleanup at: {IndexPath2}");
                    }
                    else
                    {
                        Console.WriteLine("No lock file found for IndexPath2, but lock still exists. Using fallback initialization.");
                        // Initialize without writer for read-only operations
                        analyzer2 = new StandardAnalyzer(LuceneVersion);
                        indexWriter2 = null;
                    }
                }
                catch (Exception retryEx)
                {
                    Console.WriteLine($"Failed to initialize IndexPath2 after retry: {retryEx.Message}");
                    // Initialize without writer for read-only operations
                    analyzer2 = new StandardAnalyzer(LuceneVersion);
                    indexWriter2 = null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing Lucene IndexPath2: {ex.Message}");
                // Initialize analyzer even if writer fails
                analyzer2 = new StandardAnalyzer(LuceneVersion);
                indexWriter2 = null;
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
        public void CommitIndex()
        {
            try
            {
                indexWriter.Commit();
                Console.WriteLine("Index changes committed.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error committing index: {ex.Message}");
                throw;
            }
        }
        public void IndexSingleFileWithMetadata(string filePath, dynamic metadata, bool forceReindex = false)
        {
            try
            {
                //using var scope = _serviceProvider.CreateScope();
                //var dbContext = scope.ServiceProvider.GetRequiredService<MyDbContext>();
                // Check if file exists
                if (!File.Exists(filePath))
                {
                    Console.WriteLine($"File not found: {filePath}");
                    return;
                }

                // Check if already indexed (unless force reindex)
                if (!forceReindex && IsFileAlreadyInIndex(filePath))
                {
                    Console.WriteLine($"File already indexed: {Path.GetFileName(filePath)}");
                    return;
                }

                // Delete existing document to prevent duplicates
                DeleteExistingDocument(filePath);

                // Extract file content with high-resolution capabilities for Excel files
                var content = "";
                var fileName = Path.GetFileName(filePath);
                var fileExtension = Path.GetExtension(filePath).ToLower().TrimStart('.');
                
                // Enhanced high-resolution extraction for Excel files
                if (fileExtension == "xlsx")
                {
                    content = ExtractStructuredExcelContent(filePath);
                }
                else
                {
                    content = FileTextExtractor.ExtractTextFromFile(filePath);
                }

                // Create Lucene document
                var doc = new Document();

                // File fields
                doc.Add(new TextField("filename", fileName, Field.Store.YES));
                doc.Add(new TextField("filepath", filePath, Field.Store.YES));
                doc.Add(new TextField("content", content, Field.Store.YES));
                doc.Add(new StringField("filetype", fileExtension, Field.Store.YES));
                doc.Add(new StringField("indexed_date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Field.Store.YES));
                doc.Add(new StringField("file_modified_date", File.GetLastWriteTime(filePath).ToString("yyyy-MM-dd HH:mm:ss"), Field.Store.YES));

                // Customer metadata fields
                doc.Add(new TextField("customer_id", metadata.CustomerID ?? "", Field.Store.YES));
                doc.Add(new TextField("customer_name", metadata.CustomerName ?? "", Field.Store.YES));
                doc.Add(new TextField("surname", metadata.SurName ?? "", Field.Store.YES));
                doc.Add(new TextField("customer_address", metadata.CustomerAddress ?? "", Field.Store.YES));
                doc.Add(new StringField("invoice_number", metadata.InvoiceNumber ?? "", Field.Store.YES));
                doc.Add(new TextField("merchant", metadata.Merchant ?? "", Field.Store.YES));
                doc.Add(new TextField("city", metadata.City ?? "", Field.Store.YES));
                doc.Add(new TextField("country", metadata.Country ?? "", Field.Store.YES));
                doc.Add(new TextField("phone_number", metadata.PhoneNumber ?? "", Field.Store.YES));
                doc.Add(new TextField("email_address", metadata.EmailAddress ?? "", Field.Store.YES));

                // Date fields
                if (metadata.DateOfPurchase.HasValue)
                {
                    doc.Add(new StringField("date_of_purchase", metadata.DateOfPurchase.Value.ToString("yyyy-MM-dd"), Field.Store.YES));
                    doc.Add(new Int32Field("purchase_year", metadata.DateOfPurchase.Value.Year, Field.Store.YES));
                }

                if (metadata.DateOfBirth.HasValue)
                {
                    doc.Add(new StringField("date_of_birth", metadata.DateOfBirth.Value.ToString("yyyy-MM-dd"), Field.Store.YES));
                }

                // Numeric fields
                if (metadata.RetentionPeriodYears.HasValue)
                {
                    doc.Add(new Int32Field("retention_period_years", metadata.RetentionPeriodYears.Value, Field.Store.YES));
                }

                // High-resolution Excel field indexing
                if (fileExtension == "xlsx")
                {
                    var structuredData = ExtractExcelFieldData(filePath);
                    foreach (var field in structuredData)
                    {
                        doc.Add(new TextField(field.Key.ToLower(), field.Value, Field.Store.YES));
                    }
                }

                // Create searchable full text combining all metadata
                var fullText = $"{content} {metadata.CustomerID} {metadata.CustomerName} {metadata.SurName} " +
                              $"{metadata.CustomerAddress} {metadata.InvoiceNumber} {metadata.Merchant} " +
                              $"{metadata.City} {metadata.Country} {metadata.PhoneNumber} {metadata.EmailAddress}";
                doc.Add(new TextField("full_text", fullText, Field.Store.NO));

                // Add document to index
                indexWriter.AddDocument(doc);

                // Mark as indexed
                var tracker = new FileIndexTracker(IndexPath);
                tracker.MarkFileAsIndexed(filePath);

                Console.WriteLine($"Enhanced high-resolution indexed: {fileName} for customer {metadata.CustomerID} - {metadata.CustomerName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error indexing file {Path.GetFileName(filePath)}: {ex.Message}");
                throw;
            }
        }

        // Overloaded method for Dictionary<string, string> metadata (for TRIM ContentManager)
        public void IndexSingleFileWithMetadata(string filePath, Dictionary<string, string> metadata, bool forceReindex = false)
        {
            try
            {
                // Check if file exists
                if (!File.Exists(filePath))
                {
                    Console.WriteLine($"File not found: {filePath}");
                    return;
                }

                // Check if already indexed (unless force reindex)
                if (!forceReindex && IsFileAlreadyInIndex(filePath))
                {
                    Console.WriteLine($"File already indexed: {Path.GetFileName(filePath)}");
                    return;
                }

                // Delete existing document to prevent duplicates
                DeleteExistingDocument(filePath);

                // Extract file content
                var content = "";
                var fileName = Path.GetFileName(filePath);
                var fileExtension = Path.GetExtension(filePath).ToLower().TrimStart('.');
                
                // Enhanced extraction for Excel files
                if (fileExtension == "xlsx")
                {
                    content = ExtractStructuredExcelContent(filePath);
                }
                else
                {
                    content = FileTextExtractor.ExtractTextFromFile(filePath);
                }

                // Create Lucene document
                var doc = new Document();

                // Basic file fields
                doc.Add(new TextField("filename", fileName, Field.Store.YES));
                doc.Add(new TextField("filepath", filePath, Field.Store.YES));
                doc.Add(new TextField("content", content, Field.Store.YES));
                doc.Add(new StringField("filetype", fileExtension, Field.Store.YES));
                doc.Add(new StringField("indexed_date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Field.Store.YES));
                doc.Add(new StringField("file_modified_date", File.GetLastWriteTime(filePath).ToString("yyyy-MM-dd HH:mm:ss"), Field.Store.YES));

                // Add TRIM metadata fields from dictionary
                foreach (var kvp in metadata)
                {
                    if (!string.IsNullOrEmpty(kvp.Value))
                    {
                        // Use StringField for exact matching fields, TextField for searchable content
                        if (IsExactMatchField(kvp.Key))
                        {
                            doc.Add(new StringField(kvp.Key, kvp.Value, Field.Store.YES));
                        }
                        else
                        {
                            doc.Add(new TextField(kvp.Key, kvp.Value, Field.Store.YES));
                        }
                    }
                }

                // Create searchable full text combining content and metadata
                var fullTextParts = new List<string> { content };
                fullTextParts.AddRange(metadata.Values.Where(v => !string.IsNullOrEmpty(v)));
                var fullText = string.Join(" ", fullTextParts);
                doc.Add(new TextField("full_text", fullText, Field.Store.NO));

                // Add document to index
                indexWriter.AddDocument(doc);

                // Mark as indexed
                var tracker = new FileIndexTracker(IndexPath);
                tracker.MarkFileAsIndexed(filePath);

                var uriValue = metadata.ContainsKey("URI") ? metadata["URI"] : "Unknown";
                var titleValue = metadata.ContainsKey("Title") ? metadata["Title"] : fileName;
                Console.WriteLine($"✅ TRIM record indexed: {fileName} (URI: {uriValue}) - {titleValue}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error indexing TRIM record {Path.GetFileName(filePath)}: {ex.Message}");
                throw;
            }
        }

        // Helper method to determine field types for indexing
        private bool IsExactMatchField(string fieldName)
        {
            // Fields that should be indexed as StringField for exact matching
            var exactMatchFields = new[] { "URI", "ClientId", "DateCreated", "Container", "Region", "Country" };
            return exactMatchFields.Contains(fieldName, StringComparer.OrdinalIgnoreCase);
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

                // Try comprehensive search first (includes sentences and regular documents)
                var comprehensiveQuery = BuildComprehensiveQuery(query);
                var hits = searcher.Search(comprehensiveQuery, 50).ScoreDocs;
                
                if (hits.Length > 0)
                {
                    Console.WriteLine($"Found {hits.Length} comprehensive search results");
                    
                    // Group results by file path to combine multiple sentences from the same file
                    var groupedResults = new Dictionary<string, SearchResultModel>();
                    
                    foreach (var hit in hits)
                    {
                        var doc = searcher.Doc(hit.Doc);
                        var docType = doc.Get("doc_type") ?? "document";
                        
                        if (docType == "sentence")
                        {
                            // Handle sentence results - get parent document info
                            var parentFile = doc.Get("parent_file") ?? "";
                            var parentFilename = doc.Get("parent_filename") ?? "";
                            var sentenceContent = doc.Get("sentence_content") ?? "";
                            var sentenceIndex = doc.Get("sentence_index") ?? "0";
                            
                            // Highlight the sentence content
                            var highlightedSentence = sentenceContent;
                            var queryWords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            foreach (var word in queryWords)
                            {
                                var regex = new System.Text.RegularExpressions.Regex($@"\b{System.Text.RegularExpressions.Regex.Escape(word)}\b",
                                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                highlightedSentence = regex.Replace(highlightedSentence, $"<strong>$0</strong>");
                            }
                            
                            // Group by file path
                            if (groupedResults.ContainsKey(parentFile))
                            {
                                // Add sentence to existing file result
                                groupedResults[parentFile].Snippets.Add($"Sentence {sentenceIndex}: {highlightedSentence}");
                                // Update score to highest score among sentences
                                if (hit.Score > groupedResults[parentFile].Score)
                                {
                                    groupedResults[parentFile].Score = hit.Score;
                                }
                            }
                            else
                            {
                                // Create new file result
                                groupedResults[parentFile] = new SearchResultModel
                                {
                                    FileName = parentFilename,
                                    FilePath = parentFile,
                                    Score = hit.Score,
                                    Snippets = new List<string> { $"Sentence {sentenceIndex}: {highlightedSentence}" },
                                    date = doc.Get("indexed_date") ?? DateTime.Now.ToString("yyyy-MM-dd")
                                };
                            }
                        }
                        else
                        {
                            // Handle regular document results
                            var fileName = doc.Get("filename") ?? "";
                            var filePath = doc.Get("filepath") ?? "";
                            var content = doc.Get("content") ?? "";
                            
                            // Create highlighted snippets using GetAllContentSnippets - returns multiple snippets for multiple matches
                            var snippets = GetAllContentSnippets(content, query, 250);
                            
                            // Group by file path
                            if (groupedResults.ContainsKey(filePath))
                            {
                                // Add snippets to existing file result
                                groupedResults[filePath].Snippets.AddRange(snippets);
                                // Update score to highest score
                                if (hit.Score > groupedResults[filePath].Score)
                                {
                                    groupedResults[filePath].Score = hit.Score;
                                }
                            }
                            else
                            {
                                // Create new file result
                                groupedResults[filePath] = new SearchResultModel
                                {
                                    FileName = fileName,
                                    FilePath = filePath,
                                    Score = hit.Score,
                                    Snippets = snippets,
                                    date = doc.Get("indexed_date") ?? DateTime.Now.ToString("yyyy-MM-dd")
                                };
                            }
                        }
                    }
                    
                    // Convert grouped results to list
                    resultList = groupedResults.Values.OrderByDescending(r => r.Score).ToList();
                    return resultList;
                }

                Console.WriteLine("No results found with comprehensive search");
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
                return resultList;
            }
        }

        public List<SearchResultModel> SearchFilesFromIndex2(string query)
        {
            var resultList = new List<SearchResultModel>();
            if (string.IsNullOrWhiteSpace(query))
            {
                Console.WriteLine("Please enter a valid search query.");
                return resultList;
            }

            try
            {
                // First, check if index2 exists and has content
                var indexDirectory2 = FSDirectory.Open(IndexPath2);

                if (!DirectoryReader.IndexExists(indexDirectory2))
                {
                    Console.WriteLine("Index2 does not exist. Please index some files first.");
                    return resultList;
                }

                // Commit any pending changes to ensure index is up to date (if writer is available)
                if (indexWriter2 != null)
                {
                    indexWriter2.Commit();
                }

                // ALWAYS create a fresh reader for search to see latest changes
                using var reader = DirectoryReader.Open(indexDirectory2);

                // Check if index has any documents
                if (reader.NumDocs == 0)
                {
                    Console.WriteLine("Index2 is empty. Please index some files first.");
                    return resultList;
                }

                var searcher = new IndexSearcher(reader);

                // Try comprehensive search first (includes sentences and regular documents)
                var comprehensiveQuery = BuildComprehensiveQueryForIndex2(query);
                var hits = searcher.Search(comprehensiveQuery, 50).ScoreDocs;
                
                if (hits.Length > 0)
                {
                    Console.WriteLine($"Found {hits.Length} comprehensive search results from Index2");
                    
                    // Group results by file path to combine multiple sentences from the same file
                    var groupedResults = new Dictionary<string, SearchResultModel>();
                    
                    foreach (var hit in hits)
                    {
                        var doc = searcher.Doc(hit.Doc);
                        var docType = doc.Get("doc_type") ?? "document";
                        
                        if (docType == "sentence")
                        {
                            // Handle sentence results - get parent document info
                            var parentFile = doc.Get("parent_file") ?? "";
                            var parentFilename = doc.Get("parent_filename") ?? "";
                            var sentenceContent = doc.Get("sentence_content") ?? "";
                            var sentenceIndex = doc.Get("sentence_index") ?? "0";
                            
                            // Highlight the sentence content
                            var highlightedSentence = sentenceContent;
                            var queryWords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            foreach (var word in queryWords)
                            {
                                var regex = new System.Text.RegularExpressions.Regex($@"\b{System.Text.RegularExpressions.Regex.Escape(word)}\b",
                                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                highlightedSentence = regex.Replace(highlightedSentence, $"<strong>$0</strong>");
                            }
                            
                            // Group by file path
                            if (groupedResults.ContainsKey(parentFile))
                            {
                                // Add sentence to existing file result
                                groupedResults[parentFile].Snippets.Add($"Sentence {sentenceIndex}: {highlightedSentence}");
                                // Update score to highest score among sentences
                                if (hit.Score > groupedResults[parentFile].Score)
                                {
                                    groupedResults[parentFile].Score = hit.Score;
                                }
                            }
                            else
                            {
                                // Create new file result
                                groupedResults[parentFile] = new SearchResultModel
                                {
                                    FileName = parentFilename,
                                    FilePath = parentFile,
                                    Score = hit.Score,
                                    Snippets = new List<string> { $"Sentence {sentenceIndex}: {highlightedSentence}" },
                                    date = doc.Get("indexed_date") ?? DateTime.Now.ToString("yyyy-MM-dd")
                                };
                            }
                        }
                        else
                        {
                            // Handle regular document results
                            var fileName = doc.Get("filename") ?? "";
                            var filePath = doc.Get("filepath") ?? "";
                            var content = doc.Get("content") ?? "";
                            
                            // Create highlighted snippets using GetAllContentSnippets - returns multiple snippets for multiple matches
                            var snippets = GetAllContentSnippets(content, query, 250);
                            
                            // Group by file path
                            if (groupedResults.ContainsKey(filePath))
                            {
                                // Add snippets to existing file result
                                groupedResults[filePath].Snippets.AddRange(snippets);
                                // Update score to highest score
                                if (hit.Score > groupedResults[filePath].Score)
                                {
                                    groupedResults[filePath].Score = hit.Score;
                                }
                            }
                            else
                            {
                                // Create new file result
                                groupedResults[filePath] = new SearchResultModel
                                {
                                    FileName = fileName,
                                    FilePath = filePath,
                                    Score = hit.Score,
                                    Snippets = snippets,
                                    date = doc.Get("indexed_date") ?? DateTime.Now.ToString("yyyy-MM-dd")
                                };
                            }
                        }
                    }
                    
                    // Convert grouped results to list
                    resultList = groupedResults.Values.OrderByDescending(r => r.Score).ToList();
                    return resultList;
                }

                Console.WriteLine("No results found with comprehensive search in Index2");
                return resultList;
            }
            catch (IndexNotFoundException ex)
            {
                Console.WriteLine("Index2 not found. Please index some files first.");
                Console.WriteLine($"Index2 path: {IndexPath2}");
                return resultList;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during Index2 search: {ex.Message}");
                return resultList;
            }
        }

        private Query BuildComprehensiveQueryForIndex2(string query)
        {
            var booleanQuery = new BooleanQuery();

            // 1. Search in regular document content - High boost
            var parser = new QueryParser(LuceneVersion, "content", analyzer2);
            try
            {
                var contentQuery = parser.Parse(query);
                contentQuery.Boost = 3.0f;
                booleanQuery.Add(contentQuery, Occur.SHOULD);
            }
            catch
            {
                var contentTermQuery = new TermQuery(new Term("content", query));
                contentTermQuery.Boost = 3.0f;
                booleanQuery.Add(contentTermQuery, Occur.SHOULD);
            }

            // 2. Search in filename - Medium boost
            try
            {
                var filenameQuery = parser.Parse($"filename:({query})");
                filenameQuery.Boost = 2.0f;
                booleanQuery.Add(filenameQuery, Occur.SHOULD);
            }
            catch
            {
                var filenameTermQuery = new TermQuery(new Term("filename", query));
                filenameTermQuery.Boost = 2.0f;
                booleanQuery.Add(filenameTermQuery, Occur.SHOULD);
            }

            // 3. Sentence-level search - High boost for precise matches
            var sentenceQuery = BuildSentenceQueryForIndex2(query);
            sentenceQuery.Boost = 2.5f;
            booleanQuery.Add(sentenceQuery, Occur.SHOULD);

            return booleanQuery;
        }

        private Query BuildSentenceQueryForIndex2(string query)
        {
            var booleanQuery = new BooleanQuery();
            
            // Add document type filter for sentences
            var docTypeQuery = new TermQuery(new Term("doc_type", "sentence"));
            booleanQuery.Add(docTypeQuery, Occur.MUST);

            // Search in sentence content with phrase matching
            var parser = new QueryParser(LuceneVersion, "sentence_content", analyzer2);
            try
            {
                // Try to parse as a phrase query first for better sentence matching
                var sentenceQuery = parser.Parse($"\"{query}\"");
                booleanQuery.Add(sentenceQuery, Occur.SHOULD);
                
                // Also add fuzzy matching for partial matches
                var fuzzyQuery = parser.Parse(query);
                fuzzyQuery.Boost = 0.7f; // Lower boost for fuzzy matches
                booleanQuery.Add(fuzzyQuery, Occur.SHOULD);
            }
            catch
            {
                // Fallback to term query
                var termQuery = new TermQuery(new Term("sentence_content", query));
                booleanQuery.Add(termQuery, Occur.MUST);
            }

            return booleanQuery;
        }

        // New dedicated search method for SyncedRecords from IndexDirectory2
        public List<SearchResultModel> SearchSyncedRecords(string query)
        {
            var resultList = new List<SearchResultModel>();
            if (string.IsNullOrWhiteSpace(query))
            {
                Console.WriteLine("Please enter a valid search query.");
                return resultList;
            }

            try
            {
                // Use IndexDirectory2 where SyncApplication writes the records
                var indexDirectory2 = FSDirectory.Open(IndexPath2);

                if (!DirectoryReader.IndexExists(indexDirectory2))
                {
                    Console.WriteLine("SyncedRecords index does not exist. Please run SyncApplication indexing first.");
                    return resultList;
                }

                // Commit any pending changes
                if (indexWriter2 != null)
                {
                    indexWriter2.Commit();
                }

                using var reader = DirectoryReader.Open(indexDirectory2);

                if (reader.NumDocs == 0)
                {
                    Console.WriteLine("SyncedRecords index is empty. Please run SyncApplication indexing first.");
                    return resultList;
                }

                var searcher = new IndexSearcher(reader);
                
                // Search for both master records and word documents from SyncApplication indexing
                var booleanQuery = new BooleanQuery();
                
                // Search master records (doc_type = "master_record")
                var masterRecordQuery = new BooleanQuery();
                masterRecordQuery.Add(new TermQuery(new Term("doc_type", "master_record")), Occur.MUST);
                
                // Add query for searchable content in master records
                try
                {
                    var queryParser = new QueryParser(LuceneVersion, "searchable_content", analyzer2);
                    var contentQuery = queryParser.Parse(query);
                    masterRecordQuery.Add(contentQuery, Occur.MUST);
                }
                catch
                {
                    // Fallback to wildcard search on multiple fields
                    var wildcardQuery = new BooleanQuery();
                    var fields = new[] { "Title", "Container", "Region", "Country", "ClientId", "BillTo", "ShipTo", "file_content" };
                    
                    foreach (var field in fields)
                    {
                        try
                        {
                            var fieldQuery = new WildcardQuery(new Term(field, $"*{query.ToLower()}*"));
                            wildcardQuery.Add(fieldQuery, Occur.SHOULD);
                        }
                        catch { }
                    }
                    
                    if (wildcardQuery.Clauses.Count > 0)
                        masterRecordQuery.Add(wildcardQuery, Occur.MUST);
                }
                
                booleanQuery.Add(masterRecordQuery, Occur.SHOULD);

                // Search word documents (doc_type = "word") 
                var wordDocQuery = new BooleanQuery();
                wordDocQuery.Add(new TermQuery(new Term("doc_type", "word")), Occur.MUST);
                
                try
                {
                    var wordQuery = new WildcardQuery(new Term("word", $"*{query.ToLower()}*"));
                    wordDocQuery.Add(wordQuery, Occur.MUST);
                }
                catch
                {
                    var termQuery = new TermQuery(new Term("word", query.ToLower()));
                    wordDocQuery.Add(termQuery, Occur.MUST);
                }
                
                booleanQuery.Add(wordDocQuery, Occur.SHOULD);

                var hits = searcher.Search(booleanQuery, 100).ScoreDocs;
                Console.WriteLine($"Found {hits.Length} results in SyncedRecords index");

                // Process results and combine by URI to return complete record objects
                var recordsByURI = new Dictionary<string, SearchResultModel>();
                
                foreach (var hit in hits)
                {
                    var doc = searcher.Doc(hit.Doc);
                    var docType = doc.Get("doc_type");
                    var uri = doc.Get("URI") ?? doc.Get("parent_URI") ?? "";
                    
                    if (string.IsNullOrEmpty(uri)) continue;
                    
                    if (!recordsByURI.ContainsKey(uri))
                    {
                        // Create new record result
                        var result = new SearchResultModel
                        {
                            FilePath = doc.Get("filepath") ?? doc.Get("DownloadLink") ?? "",
                            FileName = doc.Get("Title") ?? doc.Get("parent_Title") ?? "Unknown",
                            Metadata = new Dictionary<string, string>(),
                            Snippets = new List<string>(),
                            date = doc.Get("DateCreated") ?? doc.Get("indexed_date")
                        };
                        
                        // Extract all metadata fields for complete record object
                        var fieldNames = new[] { 
                            "URI", "Title", "Container", "Region", "Country", "ClientId", 
                            "BillTo", "ShipTo", "Assignee", "DateCreated", "IsContainer", 
                            "IsElectronic", "DownloadLink", "Extension", "CustomerID", "InvoiceNumber",
                            "City", "CustomerAddress", "AllParts"
                        };
                        
                        foreach (var fieldName in fieldNames)
                        {
                            var value = doc.Get(fieldName) ?? doc.Get($"parent_{fieldName}") ?? "";
                            if (!string.IsNullOrEmpty(value))
                            {
                                result.Metadata[fieldName] = value;
                            }
                        }
                        
                        recordsByURI[uri] = result;
                    }
                    
                    // Add search context/snippets
                    if (docType == "word")
                    {
                        var context = doc.Get("context");
                        if (!string.IsNullOrEmpty(context) && !recordsByURI[uri].Snippets.Contains(context))
                        {
                            recordsByURI[uri].Snippets.Add(context);
                        }
                    }
                }
                
                resultList.AddRange(recordsByURI.Values);
                Console.WriteLine($"Returning {resultList.Count} complete record objects");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error searching SyncedRecords: {ex.Message}");
            }

            return resultList;
        }

        public List<SearchResultModel> SearchFilesInPaths(string query, List<string> filePaths)
        {
            var resultList = new List<SearchResultModel>();
            if (string.IsNullOrWhiteSpace(query))
            {
                Console.WriteLine("Please enter a valid search query.");
                return resultList;
            }

            if (filePaths == null || !filePaths.Any())
            {
                Console.WriteLine("No file paths provided for targeted search.");
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

                // Build comprehensive query with file path filtering
                var comprehensiveQuery = BuildComprehensiveQueryWithPathFilter(query, filePaths);
                var hits = searcher.Search(comprehensiveQuery, 50).ScoreDocs;
                
                if (hits.Length > 0)
                {
                    Console.WriteLine($"Found {hits.Length} targeted search results in specified paths");
                    
                    // Group results by file path to combine multiple sentences from the same file
                    var groupedResults = new Dictionary<string, SearchResultModel>();
                    
                    foreach (var hit in hits)
                    {
                        var doc = searcher.Doc(hit.Doc);
                        var docType = doc.Get("doc_type") ?? "document";
                        
                        if (docType == "sentence")
                        {
                            // Handle sentence results - get parent document info
                            var parentFile = doc.Get("parent_file") ?? "";
                            var parentFilename = doc.Get("parent_filename") ?? "";
                            var sentenceContent = doc.Get("sentence_content") ?? "";
                            var sentenceIndex = doc.Get("sentence_index") ?? "0";
                            
                            // Highlight the sentence content
                            var highlightedSentence = sentenceContent;
                            var queryWords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                            foreach (var word in queryWords)
                            {
                                var regex = new System.Text.RegularExpressions.Regex($@"\b{System.Text.RegularExpressions.Regex.Escape(word)}\b",
                                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                highlightedSentence = regex.Replace(highlightedSentence, $"<strong>$0</strong>");
                            }
                            
                            // Group by file path
                            if (groupedResults.ContainsKey(parentFile))
                            {
                                // Add sentence to existing file result
                                groupedResults[parentFile].Snippets.Add($"Sentence {sentenceIndex}: {highlightedSentence}");
                                // Update score to highest score among sentences
                                if (hit.Score > groupedResults[parentFile].Score)
                                {
                                    groupedResults[parentFile].Score = hit.Score;
                                }
                            }
                            else
                            {
                                // Create new file result
                                groupedResults[parentFile] = new SearchResultModel
                                {
                                    FileName = parentFilename,
                                    FilePath = parentFile,
                                    Score = hit.Score,
                                    Snippets = new List<string> { $"Sentence {sentenceIndex}: {highlightedSentence}" },
                                    date = doc.Get("indexed_date") ?? DateTime.Now.ToString("yyyy-MM-dd")
                                };
                            }
                        }
                        else
                        {
                            // Handle regular document results
                            var fileName = doc.Get("filename") ?? "";
                            var filePath = doc.Get("filepath") ?? "";
                            var content = doc.Get("content") ?? "";
                            
                            // Create highlighted snippets using the same method as SearchFiles
                            var snippets = GetAllContentSnippets(content, query, 500);
                            
                            // Group by file path
                            if (groupedResults.ContainsKey(filePath))
                            {
                                // Add snippets to existing file result
                                groupedResults[filePath].Snippets.AddRange(snippets);
                                // Update score to highest score
                                if (hit.Score > groupedResults[filePath].Score)
                                {
                                    groupedResults[filePath].Score = hit.Score;
                                }
                            }
                            else
                            {
                                // Create new file result
                                groupedResults[filePath] = new SearchResultModel
                                {
                                    FileName = fileName,
                                    FilePath = filePath,
                                    Score = hit.Score,
                                    Snippets = snippets,
                                    date = doc.Get("indexed_date") ?? DateTime.Now.ToString("yyyy-MM-dd")
                                };
                            }
                        }
                    }
                    
                    // Convert grouped results to list
                    resultList = groupedResults.Values.OrderByDescending(r => r.Score).ToList();
                    return resultList;
                }

                Console.WriteLine("No results found with targeted path search");
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
                Console.WriteLine($"Error during targeted path search: {ex.Message}");
                return resultList;
            }
        }

        private Query BuildComprehensiveQueryWithPathFilter(string query, List<string> filePaths)
        {
            var mainBooleanQuery = new BooleanQuery();

            // Create file path filter using BooleanQuery for multiple paths
            var pathFilterQuery = new BooleanQuery();
            
            foreach (var filePath in filePaths)
            {
                // Create queries for both exact path matches and filename matches
                var exactPathQuery = new TermQuery(new Term("filepath", filePath));
                var filenameQuery = new TermQuery(new Term("filename", Path.GetFileName(filePath)));
                var parentFileQuery = new TermQuery(new Term("parent_file", filePath)); // For sentence documents
                
                var pathQuery = new BooleanQuery();
                pathQuery.Add(exactPathQuery, Occur.SHOULD);
                pathQuery.Add(filenameQuery, Occur.SHOULD);
                pathQuery.Add(parentFileQuery, Occur.SHOULD);
                
                pathFilterQuery.Add(pathQuery, Occur.SHOULD);
            }

            // Add path filter as mandatory
            mainBooleanQuery.Add(pathFilterQuery, Occur.MUST);

            // Build the same comprehensive content search as original SearchFiles method
            var contentBooleanQuery = new BooleanQuery();

            // 1. Search in regular document content - High boost
            var parser = new QueryParser(LuceneVersion, "content", analyzer);
            try
            {
                var contentQuery = parser.Parse(query);
                contentQuery.Boost = 3.0f;
                contentBooleanQuery.Add(contentQuery, Occur.SHOULD);
            }
            catch
            {
                var contentTermQuery = new TermQuery(new Term("content", query));
                contentTermQuery.Boost = 3.0f;
                contentBooleanQuery.Add(contentTermQuery, Occur.SHOULD);
            }

            // 2. Search in filename - Medium boost
            try
            {
                var filenameQuery = parser.Parse($"filename:({query})");
                filenameQuery.Boost = 2.0f;
                contentBooleanQuery.Add(filenameQuery, Occur.SHOULD);
            }
            catch
            {
                var filenameTermQuery = new TermQuery(new Term("filename", query));
                filenameTermQuery.Boost = 2.0f;
                contentBooleanQuery.Add(filenameTermQuery, Occur.SHOULD);
            }

            // 3. Sentence-level search - High boost for precise matches
            var sentenceQuery = BuildSentenceQuery(query);
            sentenceQuery.Boost = 2.5f;
            contentBooleanQuery.Add(sentenceQuery, Occur.SHOULD);

            // Add content search queries
            mainBooleanQuery.Add(contentBooleanQuery, Occur.MUST);

            return mainBooleanQuery;
        }

        private List<SearchResultModel> SearchHighResolutionIndex(IndexSearcher searcher, string query)
        {
            var resultList = new List<SearchResultModel>();

            try
            {
                // Detect query type for optimized high-resolution search
                var queryType = DetectHighResolutionQueryType(query);
                
                Query finalQuery;
                
                switch (queryType)
                {
                    case "word":
                        var wordTerm = query.Substring(5); // Remove "word:" prefix
                        finalQuery = BuildWordLevelQuery(wordTerm);
                        Console.WriteLine($"Executing word-level search for: {wordTerm}");
                        break;
                        
                    case "character":
                        var charTerm = query.Substring(10); // Remove "character:" prefix
                        finalQuery = BuildCharacterLevelQuery(charTerm);
                        Console.WriteLine($"Executing character-level search for: {charTerm}");
                        break;
                        
                    case "ngram_text":
                        var ngramTerm = query.Substring(11); // Remove "ngram_text:" prefix
                        finalQuery = BuildNGramQuery(ngramTerm);
                        Console.WriteLine($"Executing n-gram search for: {ngramTerm}");
                        break;
                        
                    case "block_type":
                        var blockTypeTerm = query.Substring(11); // Remove "block_type:" prefix
                        finalQuery = BuildContentBlockTypeQuery(blockTypeTerm);
                        Console.WriteLine($"Executing content block type search for: {blockTypeTerm}");
                        break;
                        
                    case "field_specific":
                        finalQuery = BuildMainDocumentQuery(query);
                        Console.WriteLine($"Executing field-specific search for: {query}");
                        break;
                        
                    case "sentence":
                        var sentenceTerm = query.Substring(9); // Remove "sentence:" prefix
                        finalQuery = BuildSentenceQuery(sentenceTerm);
                        Console.WriteLine($"Executing sentence-level search for: {sentenceTerm}");
                        break;
                        
                    default:
                        // Comprehensive multi-layer search (now includes sentence search)
                        finalQuery = BuildComprehensiveQuery(query);
                        Console.WriteLine($"Executing comprehensive high-resolution search for: {query}");
                        break;
                }

                // Execute search
                var hits = searcher.Search(finalQuery, 100).ScoreDocs;
                
                // Group results by file path to create comprehensive file results
                var fileResults = new Dictionary<string, SearchResultModel>();
                
                foreach (var hit in hits)
                {
                    var doc = searcher.Doc(hit.Doc);
                    var filePath = doc.Get("filepath");
                    var fileName = doc.Get("filename");
                    var docType = doc.Get("document_type");

                    if (string.IsNullOrEmpty(filePath)) continue;

                    if (!fileResults.ContainsKey(filePath))
                    {
                        // Create new result for this file
                        var mainDoc = FindMainDocument(searcher, filePath);
                        fileResults[filePath] = CreateSearchResultFromMainDoc(mainDoc, hit.Score, filePath, fileName);
                    }

                    // Add specific information based on document type
                    AddHighResolutionContext(fileResults[filePath], doc, docType, query);
                }

                resultList.AddRange(fileResults.Values.OrderByDescending(r => r.Score).Take(20));
                Console.WriteLine($"High-resolution search found {resultList.Count} results across {fileResults.Count} files");
                
                return resultList;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in high-resolution search: {ex.Message}");
                return resultList;
            }
        }

        private List<SearchResultModel> SearchStandardIndex(IndexSearcher searcher, string query)
        {
            var resultList = new List<SearchResultModel>();

            try
            {
                Console.WriteLine($"Searching with new indexing structure for query: '{query}'");
                
                // Create comprehensive query for both main documents and content blocks
                var mainDocQuery = BuildMainDocumentSearchQuery(query);
                var contentBlockQuery = BuildContentBlockSearchQuery(query);
                
                // Combine both queries
                var combinedQuery = new BooleanQuery();
                combinedQuery.Add(mainDocQuery, Occur.SHOULD);
                combinedQuery.Add(contentBlockQuery, Occur.SHOULD);

                // Execute search
                var hits = searcher.Search(combinedQuery, 100).ScoreDocs;

                if (hits.Length == 0)
                {
                    Console.WriteLine("No results found in main documents or content blocks.");
                    return resultList;
                }

                Console.WriteLine($"Found {hits.Length} document/block matches");

                // Group results by file path to consolidate
                var fileResults = new Dictionary<string, SearchResultModel>(StringComparer.OrdinalIgnoreCase);

                foreach (var hit in hits)
                {
                    try
                    {
                        var doc = searcher.Doc(hit.Doc);
                        var docType = doc.Get("type");
                        var filePath = doc.Get("file_path");
                        var fileName = doc.Get("file_name");

                        if (string.IsNullOrEmpty(filePath)) continue;

                        // Create or get existing result for this file
                        if (!fileResults.ContainsKey(filePath))
                        {
                            fileResults[filePath] = new SearchResultModel
                            {
                                FileName = fileName ?? Path.GetFileName(filePath),
                                FilePath = filePath,
                                Content = "",
                                Score = 0,
                                Snippets = new List<string>(),
                                date = doc.Get("last_modified") ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                                Metadata = new Dictionary<string, string>
                                {
                                    ["DocumentType"] = docType ?? "unknown",
                                    ["SearchType"] = "Standard Index Search"
                                }
                            };
                        }

                        var result = fileResults[filePath];
                        
                        // Update score with highest match
                        if (hit.Score > result.Score)
                        {
                            result.Score = hit.Score;
                        }

                        // Add content snippets based on document type
                        if (docType == "main")
                        {
                            var content = doc.Get("content");
                            if (!string.IsNullOrEmpty(content))
                            {
                                result.Content = content;
                                var snippets = GetAllContentSnippets(content, query, 250);
                                result.Snippets.AddRange(snippets);
                                
                                Console.WriteLine($"✓ Main document match: {fileName} (score: {hit.Score:F3})");
                            }

                            // Add structured data if available
                            var structuredData = doc.Get("structured_data");
                            if (!string.IsNullOrEmpty(structuredData))
                            {
                                result.Metadata["StructuredData"] = "Available";
                            }
                        }
                        else if (docType == "content_block")
                        {
                            var blockContent = doc.Get("block_content");
                            var blockType = doc.Get("block_type");
                            
                            if (!string.IsNullOrEmpty(blockContent))
                            {
                                // Create snippet for content block
                                var blockSnippet = $"[{blockType}] {blockContent.Substring(0, Math.Min(200, blockContent.Length))}";
                                if (blockContent.Length > 200) blockSnippet += "...";
                                
                                result.Snippets.Add(blockSnippet);
                                result.Metadata[$"Block_{blockType}_Match"] = "Found";
                                
                                Console.WriteLine($"✓ Content block match: {fileName} [{blockType}] (score: {hit.Score:F3})");
                            }
                        }
                    }
                    catch (Exception hitEx)
                    {
                        Console.WriteLine($"Error processing search hit: {hitEx.Message}");
                    }
                }

                // Convert to list, filter by score threshold, and take top results
                resultList = fileResults.Values
                    .Where(r => r.Score >= 0.1f) // Only show results with score 0.1 or higher
                    .OrderByDescending(r => r.Score)
                    .Take(20)
                    .ToList();

                // Remove duplicate snippets
                foreach (var result in resultList)
                {
                    result.Snippets = result.Snippets.Distinct().ToList();
                }

                Console.WriteLine($"Consolidated to {resultList.Count} unique file results");
                
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
                Console.WriteLine($"Error during standard search: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return resultList;
            }
        }

        private Query BuildMainDocumentSearchQuery(string query)
        {
            var booleanQuery = new BooleanQuery();
            
            // Filter for main documents only
            booleanQuery.Add(new TermQuery(new Term("type", "main")), Occur.MUST);
            
            // Search in content and structured fields
            var searchFields = new[] { "content", "structured_data" };
            
            // Add field-specific searches if available
            for (int i = 0; i < 10; i++) // Check for field_* entries
            {
                searchFields = searchFields.Concat(new[] { $"field_category", $"field_description", $"field_value", $"field_status" }).ToArray();
            }
            
            try
            {
                var parser = new MultiFieldQueryParser(LuceneVersion, searchFields, analyzer);
                var contentQuery = parser.Parse(query);
                booleanQuery.Add(contentQuery, Occur.MUST);
            }
            catch
            {
                // Fallback to simple term query if parsing fails
                booleanQuery.Add(new TermQuery(new Term("content", query)), Occur.MUST);
            }
            
            return booleanQuery;
        }

        private Query BuildContentBlockSearchQuery(string query)
        {
            var booleanQuery = new BooleanQuery();
            
            // Filter for content blocks only
            booleanQuery.Add(new TermQuery(new Term("type", "content_block")), Occur.MUST);
            
            // Search in block content
            try
            {
                var parser = new QueryParser(LuceneVersion, "block_content", analyzer);
                var blockQuery = parser.Parse(query);
                booleanQuery.Add(blockQuery, Occur.MUST);
            }
            catch
            {
                // Fallback to simple term query
                booleanQuery.Add(new TermQuery(new Term("block_content", query)), Occur.MUST);
            }
            
            return booleanQuery;
        }

        public List<SearchResultModel> SemanticSearch(string query, List<string> filePaths = null, int maxResults = 10)
        {
            var results = new List<SearchResultModel>();

            if (_embeddingService == null)
            {
                Console.WriteLine("Embedding service not available, falling back to keyword search");
                return SearchFiles(query);
            }

            if (string.IsNullOrWhiteSpace(query))
            {
                Console.WriteLine("Please enter a valid search query.");
                return results;
            }

            try
            {
                var indexDirectory = FSDirectory.Open(IndexPath);

                if (!DirectoryReader.IndexExists(indexDirectory))
                {
                    Console.WriteLine("Index does not exist. Please index some files first.");
                    return results;
                }

                // Generate query embedding
                var queryEmbedding = _embeddingService.GetEmbedding(query);

                // Commit any pending changes
                indexWriter.Commit();

                using var reader = DirectoryReader.Open(indexDirectory);
                if (reader.NumDocs == 0)
                {
                    Console.WriteLine("Index is empty. Please index some files first.");
                    return results;
                }

                var searcher = new IndexSearcher(reader);
                var chunkResults = new List<(Document doc, float similarity, string relevantText)>();

                // Search through all documents
                for (int i = 0; i < reader.MaxDoc; i++)
                {
                    Document doc;
                    try
                    {
                        doc = reader.Document(i);
                        if (doc == null) continue;
                    }
                    catch (Exception)
                    {
                        continue; // Skip deleted or inaccessible documents
                    }
                    var filePath = doc.Get("filepath");
                    
                    // Filter by specific file paths if provided
                    if (filePaths != null && filePaths.Any())
                    {
                        bool matchesPath = filePaths.Any(fp => 
                            Path.GetFileName(fp).Equals(doc.Get("filename"), StringComparison.OrdinalIgnoreCase) ||
                            fp.Equals(filePath, StringComparison.OrdinalIgnoreCase));
                        
                        if (!matchesPath) continue;
                    }

                    // Check for embeddings from OlamaApi indexing
                    var embeddingBytes = doc.GetBinaryValue("content_embeddings");
                    if (embeddingBytes != null)
                    {
                        try
                        {
                            var docEmbeddings = new float[embeddingBytes.Length / 4];
                            Buffer.BlockCopy(embeddingBytes.Bytes, 0, docEmbeddings, 0, embeddingBytes.Length);
                            
                            var similarity = TextEmbeddingService.CosineSimilarity(queryEmbedding, docEmbeddings);
                            if (similarity > 0.1f) // Threshold for relevance
                            {
                                // Get relevant text snippet
                                var content = doc.Get("content") ?? "";
                                // Use full content for complete indexing
                                var relevantText = content;
                                chunkResults.Add((doc, similarity, relevantText));
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error processing embeddings for document {i}: {ex.Message}");
                        }
                    }
                    else
                    {
                        // Fallback: Generate embedding on-the-fly for documents without pre-computed embeddings
                        var content = doc.Get("content");
                        if (!string.IsNullOrEmpty(content))
                        {
                            try
                            {
                                // Use full content for complete semantic understanding
                                var docEmbedding = _embeddingService.GetEmbedding(content);
                                var similarity = TextEmbeddingService.CosineSimilarity(queryEmbedding, docEmbedding);
                                
                                if (similarity > 0.1f)
                                {
                                    // Use full content for complete indexing
                                var relevantText = content;
                                    chunkResults.Add((doc, similarity, relevantText));
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error generating on-the-fly embedding: {ex.Message}");
                            }
                        }
                    }
                }

                // Sort by similarity and take top results
                var topResults = chunkResults
                    .OrderByDescending(x => x.similarity)
                    .Take(maxResults);

                Console.WriteLine($"\nFound {chunkResults.Count} semantically relevant result(s):\n");

                int resultCount = 0;
                foreach (var (doc, similarity, relevantText) in topResults)
                {
                    resultCount++;
                    var fileName = doc.Get("filename");
                    var filePath = doc.Get("filepath");
                    var date = doc.Get("indexed_date");

                    // Get metadata
                    var customerId = doc.Get("customer_id") ?? "";
                    var customerName = doc.Get("customer_name") ?? "";
                    var invoiceNumber = doc.Get("invoice_number") ?? "";
                    var city = doc.Get("city") ?? "";
                    var country = doc.Get("country") ?? "";
                    var dateOfPurchase = doc.Get("date_of_purchase") ?? "";

                    var snippets = GetAllContentSnippets(relevantText, query, 250);

                    var resultModel = new SearchResultModel
                    {
                        FileName = fileName,
                        FilePath = filePath,
                        Score = similarity,
                        Snippets = snippets,
                        date = date,
                        Metadata = new Dictionary<string, string>
                        {
                            ["CustomerID"] = customerId,
                            ["CustomerName"] = customerName,
                            ["InvoiceNumber"] = invoiceNumber,
                            ["City"] = city,
                            ["Country"] = country,
                            ["DateOfPurchase"] = dateOfPurchase,
                            ["SimilarityScore"] = similarity.ToString("F4"),
                            ["RelevantText"] = relevantText
                        }
                    };

                    results.Add(resultModel);
                }

                return results;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during semantic search: {ex.Message}");
                // Fallback to keyword search
                return SearchFiles(query);
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

            var queryWords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var foundPositions = new List<int>();

            // Use regex to find word boundary matches for each query word
            foreach (var word in queryWords)
            {
                var regex = new System.Text.RegularExpressions.Regex($@"\b{System.Text.RegularExpressions.Regex.Escape(word)}\b",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                var matches = regex.Matches(content);
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    foundPositions.Add(match.Index);
                }
            }

            if (foundPositions.Count == 0)
            {
                // If no word boundary matches found, create a basic snippet with highlighting attempt
                var basicSnippet = content.Length > maxLength ? content.Substring(0, maxLength) + "..." : content;
                
                // Try to highlight anyway (might catch partial matches)
                foreach (var word in queryWords)
                {
                    var regex = new System.Text.RegularExpressions.Regex($@"\b{System.Text.RegularExpressions.Regex.Escape(word)}\b",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    basicSnippet = regex.Replace(basicSnippet, $"<strong>$0</strong>");
                }
                
                snippets.Add(basicSnippet);
                return snippets;
            }

            foundPositions.Sort();
            var filteredPositions = new List<int>();

            // Filter out positions that are too close to each other
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

            // Create snippets around each found position
            foreach (var position in filteredPositions.Take(5)) // Limit to 5 snippets max
            {
                int start = Math.Max(0, position - maxLength / 2);
                int end = Math.Min(content.Length, position + maxLength / 2);

                var snippet = content.Substring(start, end - start).Trim();

                // Add ellipsis if needed
                if (start > 0) snippet = "..." + snippet;
                if (end < content.Length) snippet += "...";

                // Highlight all query words in this snippet
                foreach (var word in queryWords)
                {
                    var regex = new System.Text.RegularExpressions.Regex($@"\b{System.Text.RegularExpressions.Regex.Escape(word)}\b",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    snippet = regex.Replace(snippet, $"<strong>$0</strong>");
                }

                if (!snippets.Contains(snippet)) // Avoid duplicates
                {
                    snippets.Add(snippet);
                }
            }

            return snippets;
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
                indexWriter2?.Dispose();
                analyzer?.Dispose();
                analyzer2?.Dispose();
                _embeddingService?.Dispose();
                Console.WriteLine("Lucene.NET resources cleaned up.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during cleanup: {ex.Message}");
            }
        }

        // Advanced query type detection for optimized high-resolution search
        private string DetectHighResolutionQueryType(string query)
        {
            query = query.ToLower().Trim();
            
            if (query.StartsWith("word:"))
                return "word";
            else if (query.StartsWith("character:"))
                return "character";
            else if (query.StartsWith("ngram_text:"))
                return "ngram_text";
            else if (query.StartsWith("block_type:"))
                return "block_type";
            else if (query.StartsWith("sentence:"))
                return "sentence";
            else if (query.Contains(":") && (query.Contains("category:") || query.Contains("description:") || query.Contains("value:") || query.Contains("status:") || query.Contains("customer_name:") || query.Contains("customer_id:")))
                return "field_specific";
            else
                return "comprehensive";
        }

        private Query BuildComprehensiveQuery(string query)
        {
            var booleanQuery = new BooleanQuery();

            // 1. Search in regular document content - High boost
            var parser = new QueryParser(LuceneVersion, "content", analyzer);
            try
            {
                var contentQuery = parser.Parse(query);
                contentQuery.Boost = 3.0f;
                booleanQuery.Add(contentQuery, Occur.SHOULD);
            }
            catch
            {
                var contentTermQuery = new TermQuery(new Term("content", query));
                contentTermQuery.Boost = 3.0f;
                booleanQuery.Add(contentTermQuery, Occur.SHOULD);
            }

            // 2. Search in filename - Medium boost
            try
            {
                var filenameQuery = parser.Parse($"filename:({query})");
                filenameQuery.Boost = 2.0f;
                booleanQuery.Add(filenameQuery, Occur.SHOULD);
            }
            catch
            {
                var filenameTermQuery = new TermQuery(new Term("filename", query));
                filenameTermQuery.Boost = 2.0f;
                booleanQuery.Add(filenameTermQuery, Occur.SHOULD);
            }

            // 3. Sentence-level search - High boost for precise matches
            var sentenceQuery = BuildSentenceQuery(query);
            sentenceQuery.Boost = 2.5f;
            booleanQuery.Add(sentenceQuery, Occur.SHOULD);

            return booleanQuery;
        }

        private Query BuildContentBlockTypeQuery(string blockType)
        {
            var booleanQuery = new BooleanQuery();
            
            // Add document type filter
            var docTypeQuery = new TermQuery(new Term("document_type", "content_block"));
            booleanQuery.Add(docTypeQuery, Occur.MUST);

            // Search for specific block type
            var blockTypeQuery = new TermQuery(new Term("block_type", blockType));
            booleanQuery.Add(blockTypeQuery, Occur.MUST);

            return booleanQuery;
        }

        // High-resolution search query builders for OlamaApi compatibility
        private Query BuildMainDocumentQuery(string query)
        {
            var booleanQuery = new BooleanQuery();
            
            // Add document type filter
            var docTypeQuery = new TermQuery(new Term("document_type", "main"));
            booleanQuery.Add(docTypeQuery, Occur.MUST);

            // Add content search
            var parser = new MultiFieldQueryParser(LuceneVersion, new[] { "content", "customer_name", "customer_id", "city", "country", "category", "description", "value", "status" }, analyzer);
            
            try
            {
                var contentQuery = parser.Parse(query);
                booleanQuery.Add(contentQuery, Occur.MUST);
            }
            catch
            {
                // Fallback to simple term query
                var termQuery = new TermQuery(new Term("content", query));
                booleanQuery.Add(termQuery, Occur.MUST);
            }

            return booleanQuery;
        }

        private Query BuildWordLevelQuery(string query)
        {
            var booleanQuery = new BooleanQuery();
            
            // Add document type filter
            var docTypeQuery = new TermQuery(new Term("document_type", "word"));
            booleanQuery.Add(docTypeQuery, Occur.MUST);

            // Search in word fields
            var wordQuery = new BooleanQuery();
            wordQuery.Add(new TermQuery(new Term("word", query)), Occur.SHOULD);
            wordQuery.Add(new TermQuery(new Term("normalized_word", query.ToLower())), Occur.SHOULD);
            
            booleanQuery.Add(wordQuery, Occur.MUST);
            return booleanQuery;
        }

        private Query BuildCharacterLevelQuery(string query)
        {
            var booleanQuery = new BooleanQuery();
            
            // Add document type filter
            var docTypeQuery = new TermQuery(new Term("document_type", "character"));
            booleanQuery.Add(docTypeQuery, Occur.MUST);

            // Search for characters in the query
            if (query.Length > 0)
            {
                var charQuery = new TermQuery(new Term("character", query.Substring(0, 1)));
                booleanQuery.Add(charQuery, Occur.MUST);
            }

            return booleanQuery;
        }

        private Query BuildNGramQuery(string query)
        {
            var booleanQuery = new BooleanQuery();
            
            // Add document type filter
            var docTypeQuery = new TermQuery(new Term("document_type", "ngram"));
            booleanQuery.Add(docTypeQuery, Occur.MUST);

            // Search in n-gram text
            var ngramQuery = new TermQuery(new Term("ngram_text", query.ToLower()));
            booleanQuery.Add(ngramQuery, Occur.MUST);

            return booleanQuery;
        }

        private Query BuildContentBlockQuery(string query)
        {
            var booleanQuery = new BooleanQuery();
            
            // Add document type filter
            var docTypeQuery = new TermQuery(new Term("document_type", "content_block"));
            booleanQuery.Add(docTypeQuery, Occur.MUST);

            // Search in block content
            var parser = new QueryParser(LuceneVersion, "block_content", analyzer);
            try
            {
                var blockQuery = parser.Parse(query);
                booleanQuery.Add(blockQuery, Occur.MUST);
            }
            catch
            {
                var termQuery = new TermQuery(new Term("block_content", query));
                booleanQuery.Add(termQuery, Occur.MUST);
            }

            return booleanQuery;
        }
        
        /// <summary>
        /// Build query for searching sentence-level content
        /// </summary>
        private Query BuildSentenceQuery(string query)
        {
            var booleanQuery = new BooleanQuery();
            
            // Add document type filter for sentences
            var docTypeQuery = new TermQuery(new Term("doc_type", "sentence"));
            booleanQuery.Add(docTypeQuery, Occur.MUST);

            // Search in sentence content with phrase matching
            var parser = new QueryParser(LuceneVersion, "sentence_content", analyzer);
            try
            {
                // Try to parse as a phrase query first for better sentence matching
                var sentenceQuery = parser.Parse($"\"{query}\"");
                booleanQuery.Add(sentenceQuery, Occur.SHOULD);
                
                // Also add fuzzy matching for partial matches
                var fuzzyQuery = parser.Parse(query);
                fuzzyQuery.Boost = 0.7f; // Lower boost for fuzzy matches
                booleanQuery.Add(fuzzyQuery, Occur.SHOULD);
            }
            catch
            {
                // Fallback to term query
                var termQuery = new TermQuery(new Term("sentence_content", query));
                booleanQuery.Add(termQuery, Occur.MUST);
            }

            return booleanQuery;
        }

        private Document FindMainDocument(IndexSearcher searcher, string filePath)
        {
            try
            {
                var query = new BooleanQuery();
                query.Add(new TermQuery(new Term("document_type", "main")), Occur.MUST);
                query.Add(new TermQuery(new Term("filepath", filePath)), Occur.MUST);

                var hits = searcher.Search(query, 1).ScoreDocs;
                if (hits.Length > 0)
                {
                    return searcher.Doc(hits[0].Doc);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error finding main document: {ex.Message}");
            }

            return null;
        }

        private SearchResultModel CreateSearchResultFromMainDoc(Document mainDoc, float score, string filePath, string fileName)
        {
            if (mainDoc == null)
            {
                return new SearchResultModel
                {
                    FileName = fileName ?? Path.GetFileName(filePath),
                    FilePath = filePath,
                    Score = score,
                    Snippets = new List<string>(),
                    date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    Metadata = new Dictionary<string, string>()
                };
            }

            var metadata = new Dictionary<string, string>();
            
            // Extract all available metadata from main document
            var metadataFields = new[] { "customer_id", "customer_name", "invoice_number", "city", "country", "date_of_purchase", "total_words", "total_characters", "category", "description", "value", "status" };
            
            foreach (var field in metadataFields)
            {
                var value = mainDoc.Get(field);
                if (!string.IsNullOrEmpty(value))
                {
                    metadata[field] = value;
                }
            }

            return new SearchResultModel
            {
                FileName = mainDoc.Get("filename") ?? fileName,
                FilePath = filePath,
                Score = score,
                Snippets = new List<string>(),
                date = mainDoc.Get("indexed_date") ?? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Metadata = metadata
            };
        }

        private void AddHighResolutionContext(SearchResultModel result, Document doc, string docType, string query)
        {
            try
            {
                switch (docType)
                {
                    case "word":
                        AddWordContext(result, doc, query);
                        break;
                    case "character":
                        AddCharacterContext(result, doc, query);
                        break;
                    case "ngram":
                        AddNGramContext(result, doc, query);
                        break;
                    case "content_block":
                        AddContentBlockContext(result, doc, query);
                        break;
                    case "main":
                        AddMainDocumentContext(result, doc, query);
                        break;
                }

                // Add document type information to metadata
                if (!result.Metadata.ContainsKey("SearchType"))
                {
                    result.Metadata["SearchType"] = "High-Resolution";
                }
                
                if (!result.Metadata.ContainsKey("DocumentType"))
                {
                    result.Metadata["DocumentType"] = docType;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error adding high-resolution context: {ex.Message}");
            }
        }

        private void AddWordContext(SearchResultModel result, Document doc, string query)
        {
            var word = doc.Get("word");
            var context = doc.Get("context");
            var position = doc.Get("start_position");
            var lineNumber = doc.Get("line_number");

            if (!string.IsNullOrEmpty(word))
            {
                var snippet = $"Word: **{word}** at line {lineNumber}, position {position}";
                if (!string.IsNullOrEmpty(context))
                {
                    snippet += $" - Context: {context}";
                }
                result.Snippets.Add(snippet);
            }
        }

        private void AddCharacterContext(SearchResultModel result, Document doc, string query)
        {
            var character = doc.Get("character");
            var frequency = doc.Get("frequency");
            var positions = doc.Get("positions");

            if (!string.IsNullOrEmpty(character))
            {
                var snippet = $"Character: **{character}** found {frequency} times";
                if (!string.IsNullOrEmpty(positions))
                {
                    var posArray = positions.Split(',').Take(5);
                    snippet += $" at positions: {string.Join(", ", posArray)}";
                }
                result.Snippets.Add(snippet);
            }
        }

        private void AddNGramContext(SearchResultModel result, Document doc, string query)
        {
            var ngram = doc.Get("ngram_text");
            var n = doc.Get("ngram_n");
            var frequency = doc.Get("frequency");

            if (!string.IsNullOrEmpty(ngram))
            {
                var snippet = $"{n}-gram: **{ngram}** (frequency: {frequency})";
                result.Snippets.Add(snippet);
            }
        }

        private void AddContentBlockContext(SearchResultModel result, Document doc, string query)
        {
            var blockContent = doc.Get("block_content");
            var blockType = doc.Get("block_type");

            if (!string.IsNullOrEmpty(blockContent))
            {
                var snippet = $"{blockType} block: {blockContent.Substring(0, Math.Min(200, blockContent.Length))}";
                if (blockContent.Length > 200) snippet += "...";
                
                // Highlight the query term
                snippet = snippet.Replace(query, $"**{query}**", StringComparison.OrdinalIgnoreCase);
                result.Snippets.Add(snippet);
            }
        }

        private void AddMainDocumentContext(SearchResultModel result, Document doc, string query)
        {
            var content = doc.Get("content");
            if (!string.IsNullOrEmpty(content))
            {
                var snippets = GetAllContentSnippets(content, query, 250);
                result.Snippets.AddRange(snippets);
            }
        }

        // High-resolution Excel content extraction methods
        private string ExtractStructuredExcelContent(string filePath)
        {
            try
            {
                using var package = new OfficeOpenXml.ExcelPackage(new FileInfo(filePath));
                var contentBuilder = new StringBuilder();

                foreach (var worksheet in package.Workbook.Worksheets)
                {
                    contentBuilder.AppendLine($"=== Sheet: {worksheet.Name} ===");
                    
                    if (worksheet.Dimension != null)
                    {
                        var startRow = worksheet.Dimension.Start.Row;
                        var endRow = worksheet.Dimension.End.Row;
                        var startCol = worksheet.Dimension.Start.Column;
                        var endCol = worksheet.Dimension.End.Column;

                        // Extract headers first
                        var headers = new List<string>();
                        for (int col = startCol; col <= endCol; col++)
                        {
                            var headerValue = worksheet.Cells[startRow, col].Text;
                            headers.Add(string.IsNullOrEmpty(headerValue) ? $"Column{col}" : headerValue);
                        }

                        // Extract data rows
                        for (int row = startRow + 1; row <= endRow; row++)
                        {
                            var rowData = new List<string>();
                            for (int col = startCol; col <= endCol; col++)
                            {
                                var cellValue = worksheet.Cells[row, col].Text;
                                rowData.Add(cellValue ?? "");
                                
                                // Create field-specific content for indexing
                                if (!string.IsNullOrEmpty(cellValue) && col - startCol < headers.Count)
                                {
                                    var fieldName = headers[col - startCol].ToLower().Replace(" ", "_");
                                    contentBuilder.AppendLine($"{fieldName}: {cellValue}");
                                }
                            }
                            contentBuilder.AppendLine(string.Join(" | ", rowData));
                        }
                    }
                }

                return contentBuilder.ToString();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting structured Excel content: {ex.Message}");
                return FileTextExtractor.ExtractTextFromFile(filePath); // Fallback
            }
        }

        private Dictionary<string, string> ExtractExcelFieldData(string filePath)
        {
            var fieldData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using var package = new OfficeOpenXml.ExcelPackage(new FileInfo(filePath));
                
                foreach (var worksheet in package.Workbook.Worksheets)
                {
                    if (worksheet.Dimension != null)
                    {
                        var startRow = worksheet.Dimension.Start.Row;
                        var endRow = worksheet.Dimension.End.Row;
                        var startCol = worksheet.Dimension.Start.Column;
                        var endCol = worksheet.Dimension.End.Column;

                        // Get headers
                        var headers = new List<string>();
                        for (int col = startCol; col <= endCol; col++)
                        {
                            var headerValue = worksheet.Cells[startRow, col].Text;
                            headers.Add(string.IsNullOrEmpty(headerValue) ? $"Column{col}" : headerValue);
                        }

                        // Extract field data for each column
                        for (int col = startCol; col <= endCol; col++)
                        {
                            var fieldName = headers[col - startCol].ToLower().Replace(" ", "_");
                            var fieldValues = new List<string>();

                            for (int row = startRow + 1; row <= endRow; row++)
                            {
                                var cellValue = worksheet.Cells[row, col].Text;
                                if (!string.IsNullOrEmpty(cellValue))
                                {
                                    fieldValues.Add(cellValue);
                                }
                            }

                            if (fieldValues.Any())
                            {
                                fieldData[fieldName] = string.Join(" ", fieldValues);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error extracting Excel field data: {ex.Message}");
            }

            return fieldData;
        }

        // IDisposable implementation
        public void Dispose()
        {
            CleanupLucene();
        }

        public async Task<bool> ProcessFilesInDirectory(string directoryPath)
        {
            try
            {
                var directoryInfo = new DirectoryInfo(directoryPath);
                if (!directoryInfo.Exists)
                {
                    Console.WriteLine($"Directory not found: {directoryPath}");
                    return false;
                }

                var files = Directory.GetFiles(directoryPath);
                Console.WriteLine($"Found {files.Length} files in directory: {directoryPath}");

                var filesToIndex = new List<string>();
                foreach (var file in files)
                {
                    if (IsIndexableFile(file))
                    {
                        filesToIndex.Add(file);
                        Console.WriteLine($"Added to indexing queue: {Path.GetFileName(file)}");
                    }
                }

                if (filesToIndex.Any())
                {
                    Console.WriteLine($"Indexing {filesToIndex.Count} files");
                    IndexMultipleFiles(filesToIndex);
                    return true;
                }

                Console.WriteLine("No indexable files found in directory");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing directory {directoryPath}: {ex.Message}");
                return false;
            }
        }
        private bool IsIndexableFile(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLower();
            var indexableExtensions = new[] { ".pdf", ".txt", ".docx", ".xlsx", ".pptx", ".csv" };
            return indexableExtensions.Contains(extension);
        }
        public void IndexMultipleFiles(List<string> filePaths)
        {
            Console.WriteLine($"Starting batch high-resolution indexing for {filePaths.Count} files");

            foreach (var filePath in filePaths)
            {
                IndexFile(filePath);
            }

            Console.WriteLine("Batch high-resolution indexing completed");
        }
        public void IndexFile(string filePath)
        {
            var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                if (!File.Exists(filePath))
                {
                    Console.WriteLine($"File not found: {filePath}");
                    return;
                }

                Console.WriteLine($"⏱️ Starting OPTIMIZED indexing for: {Path.GetFileName(filePath)}");
                
                // ===== CONTENT EXTRACTION TIMING =====
                var extractionStopwatch = System.Diagnostics.Stopwatch.StartNew();
                var content = FileTextExtractor.ExtractTextFromFile(filePath);
                extractionStopwatch.Stop();
                Console.WriteLine($"📄 Content extraction completed in: {extractionStopwatch.ElapsedMilliseconds:N0} ms");

                if (string.IsNullOrEmpty(content))
                {
                    Console.WriteLine($"No content extracted from: {filePath}");
                    return;
                }

                // ===== DOCUMENT PREPARATION TIMING =====
                var prepStopwatch = System.Diagnostics.Stopwatch.StartNew();
                
                // Delete existing document to prevent duplicates
                DeleteExistingDocument(filePath);

                // Create STORAGE-OPTIMIZED Lucene document
                var doc = new Document();
                var fileName = Path.GetFileName(filePath);
                var fileExtension = Path.GetExtension(filePath).ToLower().TrimStart('.');
                var indexedDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var modifiedDate = File.GetLastWriteTime(filePath).ToString("yyyy-MM-dd HH:mm:ss");

                // ===== ESSENTIAL FIELDS =====
                doc.Add(new TextField("filename", fileName, Field.Store.YES));
                doc.Add(new StringField("filepath", filePath, Field.Store.YES));
                doc.Add(new StringField("filetype", fileExtension, Field.Store.YES));
                doc.Add(new StringField("indexed_date", indexedDate, Field.Store.YES));
                doc.Add(new StringField("file_modified_date", modifiedDate, Field.Store.YES));

                // Store AND index FULL content
                doc.Add(new TextField("content", content, Field.Store.YES));
                
                prepStopwatch.Stop();
                Console.WriteLine($"📋 Document preparation completed in: {prepStopwatch.ElapsedMilliseconds:N0} ms");


                // ===== WORD-BY-WORD INDEXING SECTION =====
                var wordStopwatch = System.Diagnostics.Stopwatch.StartNew();
                Console.WriteLine($"🔤 Starting word-by-word indexing for: {fileName}");
                var words = ExtractWords(content);
                var wordPositionMap = new Dictionary<string, List<int>>();
                
                // Build word position mapping
                for (int i = 0; i < words.Count; i++)
                {
                    var word = words[i].ToLower().Trim();
                    if (!string.IsNullOrEmpty(word) && word.Length > 1) // Skip single characters and empty
                    {
                        if (!wordPositionMap.ContainsKey(word))
                            wordPositionMap[word] = new List<int>();
                        wordPositionMap[word].Add(i);
                    }
                }

                // Create word documents
                foreach (var wordEntry in wordPositionMap)
                {
                    var word = wordEntry.Key;
                    var positions = wordEntry.Value;
                    
                    var wordDoc = new Document();
                    wordDoc.Add(new StringField("doc_type", "word", Field.Store.YES));
                    wordDoc.Add(new StringField("parent_file", filePath, Field.Store.YES));
                    wordDoc.Add(new StringField("parent_filename", fileName, Field.Store.YES));
                    wordDoc.Add(new TextField("word", word, Field.Store.YES));
                    wordDoc.Add(new TextField("word_normalized", word.ToLower(), Field.Store.YES));
                    wordDoc.Add(new Int32Field("frequency", positions.Count, Field.Store.YES));
                    wordDoc.Add(new TextField("positions", string.Join(",", positions), Field.Store.YES));
                    wordDoc.Add(new StringField("filetype", fileExtension, Field.Store.YES));
                    
                    // Add context for first occurrence
                    if (positions.Count > 0)
                    {
                        var firstPos = positions[0];
                        var contextStart = Math.Max(0, firstPos - 5);
                        var contextEnd = Math.Min(words.Count, firstPos + 6);
                        var context = string.Join(" ", words.Skip(contextStart).Take(contextEnd - contextStart));
                        wordDoc.Add(new TextField("context", context, Field.Store.YES));
                        wordDoc.Add(new Int32Field("first_position", firstPos, Field.Store.YES));
                    }
                    
                    indexWriter.AddDocument(wordDoc);
                }
                
                wordStopwatch.Stop();
                Console.WriteLine($"✅ Word-by-word indexing completed: {wordPositionMap.Count} unique words indexed in {wordStopwatch.ElapsedMilliseconds:N0} ms ({wordStopwatch.Elapsed.TotalSeconds:F2}s)");

                // ===== SENTENCE-BY-SENTENCE INDEXING SECTION =====
                var sentenceStopwatch = System.Diagnostics.Stopwatch.StartNew();
                Console.WriteLine($"📝 Starting sentence-by-sentence indexing for: {fileName}");
                var sentences = SplitIntoSentences(content);
                foreach (var (sentence, index) in sentences.Select((s, i) => (s, i)))
                {
                    if (!string.IsNullOrWhiteSpace(sentence))
                    {
                        var sentenceDoc = new Document();
                        sentenceDoc.Add(new StringField("doc_type", "sentence", Field.Store.YES));
                        sentenceDoc.Add(new StringField("parent_file", filePath, Field.Store.YES));
                        sentenceDoc.Add(new StringField("parent_filename", fileName, Field.Store.YES));
                        sentenceDoc.Add(new TextField("sentence_content", sentence.Trim(), Field.Store.YES));
                        sentenceDoc.Add(new Int32Field("sentence_index", index, Field.Store.YES));
                        sentenceDoc.Add(new StringField("filetype", fileExtension, Field.Store.YES));

                        if (index > 0)
                            sentenceDoc.Add(new TextField("previous_sentence", sentences[index - 1].Trim(), Field.Store.YES));
                        if (index < sentences.Count - 1)
                            sentenceDoc.Add(new TextField("next_sentence", sentences[index + 1].Trim(), Field.Store.YES));

                        indexWriter.AddDocument(sentenceDoc);
                    }
                }
                
                sentenceStopwatch.Stop();
                Console.WriteLine($"✅ Sentence-by-sentence indexing completed: {sentences.Count} sentences indexed in {sentenceStopwatch.ElapsedMilliseconds:N0} ms ({sentenceStopwatch.Elapsed.TotalSeconds:F2}s)");

                // ===== FINAL COMMIT TIMING =====
                var commitStopwatch = System.Diagnostics.Stopwatch.StartNew();
                
                // File size info
                var fileInfo = new FileInfo(filePath);
                doc.Add(new Int64Field("file_size", fileInfo.Length, Field.Store.YES));

                // Add document to index
                indexWriter.AddDocument(doc);
                indexWriter.Commit();
                
                commitStopwatch.Stop();
                Console.WriteLine($"💾 Index commit completed in: {commitStopwatch.ElapsedMilliseconds:N0} ms");
                
                // ===== TOTAL TIMING SUMMARY =====
                totalStopwatch.Stop();
                var totalSeconds = totalStopwatch.Elapsed.TotalSeconds;
                var fileSizeMB = fileInfo.Length / (1024.0 * 1024.0);
                
                Console.WriteLine($"🎯 TOTAL INDEXING TIME for {fileName}: {totalStopwatch.ElapsedMilliseconds:N0} ms ({totalSeconds:F2}s)");
                Console.WriteLine($"📊 File size: {fileSizeMB:F2} MB | Processing speed: {(fileSizeMB/totalSeconds):F2} MB/s");
                Console.WriteLine($"📈 Performance: {(words?.Count ?? 0):N0} words & {(sentences?.Count ?? 0):N0} sentences processed");
                Console.WriteLine("=".PadRight(80, '='));
            }
            catch (Exception ex)
            {
                totalStopwatch.Stop();
                Console.WriteLine($"❌ Error indexing file {filePath} after {totalStopwatch.ElapsedMilliseconds:N0} ms: {ex.Message}");
            }
        }

        /// <summary>
        /// Extract words from text for word-by-word indexing
        /// </summary>
        private List<string> ExtractWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();

            // Split text into words using regex to handle punctuation properly
            var words = new List<string>();
            var wordPattern = @"\b[\w']+\b"; // Matches word boundaries including apostrophes
            var matches = System.Text.RegularExpressions.Regex.Matches(text, wordPattern);
            
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var word = match.Value.Trim();
                if (!string.IsNullOrWhiteSpace(word) && word.Length > 1)
                {
                    words.Add(word);
                }
            }
            
            return words;
        }

        /// <summary>
        /// Split text into sentences for precise indexing
        /// </summary>
        private List<string> SplitIntoSentences(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();
                
            // Split on sentence endings, but keep the delimiter
            var sentences = new List<string>();
            var currentSentence = new StringBuilder();
            
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                currentSentence.Append(c);
                
                // Check for sentence ending
                if (c == '.' || c == '!' || c == '?')
                {
                    // Look ahead to avoid splitting on abbreviations
                    if (i < text.Length - 1)
                    {
                        char next = text[i + 1];
                        // If next char is space/newline and then uppercase, it's likely sentence end
                        if (char.IsWhiteSpace(next))
                        {
                            if (i + 2 < text.Length && char.IsUpper(text[i + 2]))
                            {
                                sentences.Add(currentSentence.ToString().Trim());
                                currentSentence.Clear();
                            }
                        }
                    }
                    else
                    {
                        // End of text
                        sentences.Add(currentSentence.ToString().Trim());
                        currentSentence.Clear();
                    }
                }
                // Split on paragraph breaks as well
                else if (c == '\n' && i < text.Length - 1 && text[i + 1] == '\n')
                {
                    if (currentSentence.Length > 10) // Only if we have substantial content
                    {
                        sentences.Add(currentSentence.ToString().Trim());
                        currentSentence.Clear();
                    }
                }
            }
            
            // Add remaining content
            if (currentSentence.Length > 0)
            {
                sentences.Add(currentSentence.ToString().Trim());
            }
            
            // Filter out very short sentences and clean up
            return sentences
                .Where(s => !string.IsNullOrWhiteSpace(s) && s.Length > 10)
                .Select(s => s.Trim())
                .ToList();
        }
    }
}

