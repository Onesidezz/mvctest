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
        private IndexWriter indexWriter;
        private StandardAnalyzer analyzer;
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

            InitializeLucene();
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

                //// Enhanced search to handle all OlamaApi high-resolution document types
                var results = SearchHighResolutionIndex(searcher, query);

                if (results.Any())
                {
                    Console.WriteLine($"Found {results.Count} high-resolution results");
                    return results;
                }

                // Fallback to standard search if no high-resolution results found
                return SearchStandardIndex(searcher, query);
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
                        
                    default:
                        // Comprehensive multi-layer search
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
                                var relevantText = content.Length > 500 ? content.Substring(0, 500) + "..." : content;
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
                                var docEmbedding = _embeddingService.GetEmbedding(content.Length > 1000 ? content.Substring(0, 1000) : content);
                                var similarity = TextEmbeddingService.CosineSimilarity(queryEmbedding, docEmbedding);
                                
                                if (similarity > 0.1f)
                                {
                                    var relevantText = content.Length > 500 ? content.Substring(0, 500) + "..." : content;
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
            else if (query.Contains(":") && (query.Contains("category:") || query.Contains("description:") || query.Contains("value:") || query.Contains("status:") || query.Contains("customer_name:") || query.Contains("customer_id:")))
                return "field_specific";
            else
                return "comprehensive";
        }

        private Query BuildComprehensiveQuery(string query)
        {
            var booleanQuery = new BooleanQuery();

            // 1. Main document search (document_type:main) - Higher boost
            var mainQuery = BuildMainDocumentQuery(query);
            mainQuery.Boost = 3.0f;
            booleanQuery.Add(mainQuery, Occur.SHOULD);

            // 2. Word-level search (document_type:word) - Medium boost
            var wordQuery = BuildWordLevelQuery(query);
            wordQuery.Boost = 2.0f;
            booleanQuery.Add(wordQuery, Occur.SHOULD);

            // 3. N-gram search (document_type:ngram) - Medium boost for phrases
            var ngramQuery = BuildNGramQuery(query);
            ngramQuery.Boost = 2.0f;
            booleanQuery.Add(ngramQuery, Occur.SHOULD);

            // 4. Content block search (document_type:content_block) - Standard boost
            var blockQuery = BuildContentBlockQuery(query);
            blockQuery.Boost = 1.5f;
            booleanQuery.Add(blockQuery, Occur.SHOULD);

            // 5. Character-level search (document_type:character) - Lower boost for precision
            if (query.Length <= 3) // Only for short queries to avoid noise
            {
                var charQuery = BuildCharacterLevelQuery(query);
                charQuery.Boost = 1.0f;
                booleanQuery.Add(charQuery, Occur.SHOULD);
            }

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


    }
}

