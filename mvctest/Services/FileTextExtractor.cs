using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using mvctest.Models;
using System.Text;
using System.Text.RegularExpressions;
namespace mvctest.Services
{
    public static class FileTextExtractor
    {
        public static string ExtractTextFromFile(string filePath)
        {
            // Check file size and provide warnings for large files
            var fileInfo = new FileInfo(filePath);
            const long warningSize = 50 * 1024 * 1024; // 50MB warning threshold
            const long maxSize = 500 * 1024 * 1024; // 500MB hard limit
            
            var fileSizeMB = fileInfo.Length / 1024 / 1024;
            
            if (fileInfo.Length > maxSize)
            {
                return $"[FILE TOO LARGE] File size ({fileSizeMB}MB) exceeds maximum limit ({maxSize / 1024 / 1024}MB). Please contact administrator for processing large files.";
            }

            string extension = Path.GetExtension(filePath).ToLower();
            string content;

            try
            {
                content = extension switch
                {
                    ".txt" => ExtractTextFromTxtStreaming(filePath),
                    ".pdf" => ExtractTextFromPdf(filePath),
                    ".docx" => ExtractTextFromDocx(filePath),
                    ".xlsx" => ExtractTextFromXlsx(filePath),
                    ".pptx" => ExtractTextFromPptx(filePath),
                    _ => throw new NotSupportedException($"File type '{extension}' is not supported."),
                };

                // Add size warning to large files
                if (fileInfo.Length > warningSize)
                {
                    content = $"[LARGE FILE WARNING] File size: {fileSizeMB}MB. Processing may take longer.\n\n" + content;
                }

                return content;
            }
            catch (OutOfMemoryException)
            {
                return $"[MEMORY ERROR] File is too large to process in memory ({fileSizeMB}MB). Please use a smaller file or contact administrator.";
            }
            catch (Exception ex)
            {
                return $"[PROCESSING ERROR] Failed to extract text from file: {ex.Message}";
            }
        }

        private static string ExtractTextFromTxtStreaming(string filePath)
        {
            // Use memory-efficient streaming approach for text files
            const int bufferSize = 8192; // 8KB buffer
            var content = new StringBuilder();
            
            try
            {
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize);
                using var reader = new StreamReader(fileStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize);
                
                var buffer = new char[bufferSize];
                int charsRead;
                
                while ((charsRead = reader.Read(buffer, 0, bufferSize)) > 0)
                {
                    content.Append(buffer, 0, charsRead);
                    
                    // Allow garbage collection for very large files
                    if (content.Length % (bufferSize * 100) == 0) // Every ~800KB
                    {
                        GC.Collect(0, GCCollectionMode.Optimized);
                    }
                }
                
                return content.ToString();
            }
            catch (UnauthorizedAccessException)
            {
                return "[ACCESS ERROR] Unable to read file. Check file permissions.";
            }
            catch (FileNotFoundException)
            {
                return "[FILE ERROR] File not found.";
            }
            catch (IOException ex)
            {
                return $"[IO ERROR] Error reading file: {ex.Message}";
            }
        }
        private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".pdf", ".docx", ".xlsx", ".pptx"
        };
        public static bool IsFileTypeSupported(string filePath)
        {
            string extension = Path.GetExtension(filePath);
            return SupportedExtensions.Contains(extension);
        }
        private static string ExtractTextFromPdf(string filePath)
        {
            try
            {
                using var pdfReader = new PdfReader(filePath);
                using var pdfDoc = new PdfDocument(pdfReader);

                var text = new StringBuilder();
                int totalPages = pdfDoc.GetNumberOfPages();
                
                // Limit pages for very large PDFs to prevent memory issues
                const int maxPages = 1000;
                int pagesToProcess = Math.Min(totalPages, maxPages);
                
                if (totalPages > maxPages)
                {
                    text.AppendLine($"[LARGE PDF WARNING] PDF has {totalPages} pages. Processing first {maxPages} pages only.\n");
                }
                
                for (int pageNum = 1; pageNum <= pagesToProcess; pageNum++)
                {
                    try
                    {
                        var page = pdfDoc.GetPage(pageNum);

                        // Use advanced text extraction strategy for better accuracy
                        var strategy = new LocationTextExtractionStrategy();
                        var pageText = PdfTextExtractor.GetTextFromPage(page, strategy);

                        // Add page separator for better indexing
                        text.AppendLine($"[PAGE {pageNum}]");
                        text.AppendLine(pageText);
                        text.AppendLine($"[END PAGE {pageNum}]");
                        text.AppendLine();
                        
                        // Force garbage collection every 50 pages for large PDFs
                        if (pageNum % 50 == 0)
                        {
                            GC.Collect(0, GCCollectionMode.Optimized);
                        }
                    }
                    catch (Exception ex)
                    {
                        text.AppendLine($"[PAGE ERROR {pageNum}] Failed to extract page: {ex.Message}");
                    }
                }
                
                if (totalPages > maxPages)
                {
                    text.AppendLine($"\n[TRUNCATED] Remaining {totalPages - maxPages} pages not processed due to size limits.");
                }
                
                return text.ToString();
            }
            catch (Exception ex)
            {
                return $"[PDF ERROR] Failed to process PDF: {ex.Message}";
            }
        }

        public static HighResolutionDocument ExtractTextFromPdfHighRes(string filePath)
        {
            var content = new StringBuilder();
            var contentBlocks = new List<ContentBlock>();

            using var pdfReader = new PdfReader(filePath);
            using var pdfDoc = new PdfDocument(pdfReader);

            int globalPosition = 0;

            for (int pageNum = 1; pageNum <= pdfDoc.GetNumberOfPages(); pageNum++)
            {
                var page = pdfDoc.GetPage(pageNum);
                var strategy = new LocationTextExtractionStrategy();
                var pageText = PdfTextExtractor.GetTextFromPage(page, strategy);

                var pageHeader = $"[PAGE {pageNum}]";
                var pageFooter = $"[END PAGE {pageNum}]";

                // Add page block
                var pageBlock = new ContentBlock
                {
                    Content = pageText,
                    StartPosition = globalPosition + pageHeader.Length + 2,
                    EndPosition = globalPosition + pageHeader.Length + pageText.Length + 1,
                    Type = ContentBlockType.Paragraph,
                    Properties = new Dictionary<string, string>
                    {
                        ["page_number"] = pageNum.ToString(),
                        ["word_count"] = CountWords(pageText).ToString(),
                        ["character_count"] = pageText.Length.ToString()
                    }
                };

                contentBlocks.Add(pageBlock);

                content.AppendLine(pageHeader);
                content.AppendLine(pageText);
                content.AppendLine(pageFooter);
                content.AppendLine();

                globalPosition = content.Length;
            }

            var analyzer = new HighResolutionTextAnalyzer();
            var document = analyzer.AnalyzeDocument(
                content.ToString(),
                filePath,
                Path.GetFileName(filePath),
                "pdf"
            );

            // Add PDF-specific content blocks
            document.ContentBlocks.AddRange(contentBlocks);

            return document;
        }

        private static int CountWords(string text)
        {
            return Regex.Matches(text, @"\b\w+\b").Count;
        }

        private static string ExtractTextFromDocx(string filePath)
        {
            try
            {
                using var doc = WordprocessingDocument.Open(filePath, false);
                var content = new StringBuilder();

                if (doc.MainDocumentPart?.Document?.Body == null)
                {
                    return "[DOCX ERROR] Invalid Word document structure.";
                }

                // Extract main document content with structure preservation
                var body = doc.MainDocumentPart.Document.Body;
                var paragraphs = body.Elements<DocumentFormat.OpenXml.Wordprocessing.Paragraph>().ToList();
                
                // Limit paragraphs for very large documents
                const int maxParagraphs = 5000;
                const int maxTablesPerDoc = 100;
                const int maxListsPerDoc = 100;
                
                int totalParagraphs = paragraphs.Count;
                int paragraphsToProcess = Math.Min(totalParagraphs, maxParagraphs);
                
                if (totalParagraphs > maxParagraphs)
                {
                    content.AppendLine($"[LARGE DOCX WARNING] Word document has {totalParagraphs} paragraphs. Processing first {maxParagraphs} paragraphs only.\n");
                }

                // Process paragraphs with structure preservation
                int processedParagraphs = 0;
                foreach (var paragraph in paragraphs.Take(paragraphsToProcess))
                {
                    try
                    {
                        var paragraphText = paragraph.InnerText?.Trim();
                        if (!string.IsNullOrWhiteSpace(paragraphText))
                        {
                            // Check if it's a heading by looking at style
                            var pPr = paragraph.Elements<DocumentFormat.OpenXml.Wordprocessing.ParagraphProperties>().FirstOrDefault();
                            var pStyle = pPr?.Elements<DocumentFormat.OpenXml.Wordprocessing.ParagraphStyleId>().FirstOrDefault();
                            
                            if (pStyle != null && pStyle.Val?.Value?.Contains("Heading") == true)
                            {
                                content.AppendLine($"[HEADING] {paragraphText}");
                            }
                            else
                            {
                                content.AppendLine(paragraphText);
                            }
                        }
                        
                        processedParagraphs++;
                        
                        // Force garbage collection every 500 paragraphs for large documents
                        if (processedParagraphs % 500 == 0)
                        {
                            GC.Collect(0, GCCollectionMode.Optimized);
                        }
                    }
                    catch (Exception ex)
                    {
                        content.AppendLine($"[PARAGRAPH ERROR] Failed to extract paragraph: {ex.Message}");
                    }
                }

                // Extract tables with limits
                var tables = body.Elements<DocumentFormat.OpenXml.Wordprocessing.Table>().Take(maxTablesPerDoc);
                if (tables.Any())
                {
                    content.AppendLine("\n[TABLES]");
                    int tableCount = 0;
                    
                    foreach (var table in tables)
                    {
                        try
                        {
                            content.AppendLine($"[TABLE {++tableCount}]");
                            var rows = table.Elements<DocumentFormat.OpenXml.Wordprocessing.TableRow>().Take(50); // Limit rows per table
                            
                            foreach (var row in rows)
                            {
                                var cells = row.Elements<DocumentFormat.OpenXml.Wordprocessing.TableCell>().Take(20); // Limit columns per row
                                var cellTexts = cells.Select(cell => cell.InnerText?.Trim() ?? "").Where(text => !string.IsNullOrWhiteSpace(text));
                                
                                if (cellTexts.Any())
                                {
                                    content.AppendLine(string.Join(" | ", cellTexts));
                                }
                            }
                            content.AppendLine($"[END TABLE {tableCount}]");
                        }
                        catch (Exception ex)
                        {
                            content.AppendLine($"[TABLE ERROR {tableCount}] Failed to extract table: {ex.Message}");
                        }
                    }
                    content.AppendLine("[END TABLES]");
                }

                // Extract footnotes if present (limited)
                var footnotesPart = doc.MainDocumentPart.FootnotesPart;
                if (footnotesPart?.Footnotes != null)
                {
                    try
                    {
                        content.AppendLine("\n[FOOTNOTES]");
                        var footnotes = footnotesPart.Footnotes.Elements<DocumentFormat.OpenXml.Wordprocessing.Footnote>().Take(50);
                        
                        foreach (var footnote in footnotes)
                        {
                            var footnoteText = footnote.InnerText?.Trim();
                            if (!string.IsNullOrWhiteSpace(footnoteText))
                            {
                                content.AppendLine($"  {footnoteText}");
                            }
                        }
                        content.AppendLine("[END FOOTNOTES]");
                    }
                    catch (Exception ex)
                    {
                        content.AppendLine($"[FOOTNOTES ERROR] Failed to extract footnotes: {ex.Message}");
                    }
                }

                // Extract endnotes if present (limited)
                var endnotesPart = doc.MainDocumentPart.EndnotesPart;
                if (endnotesPart?.Endnotes != null)
                {
                    try
                    {
                        content.AppendLine("\n[ENDNOTES]");
                        var endnotes = endnotesPart.Endnotes.Elements<DocumentFormat.OpenXml.Wordprocessing.Endnote>().Take(50);
                        
                        foreach (var endnote in endnotes)
                        {
                            var endnoteText = endnote.InnerText?.Trim();
                            if (!string.IsNullOrWhiteSpace(endnoteText))
                            {
                                content.AppendLine($"  {endnoteText}");
                            }
                        }
                        content.AppendLine("[END ENDNOTES]");
                    }
                    catch (Exception ex)
                    {
                        content.AppendLine($"[ENDNOTES ERROR] Failed to extract endnotes: {ex.Message}");
                    }
                }

                // Extract comments if present (limited)
                var commentsPart = doc.MainDocumentPart.WordprocessingCommentsPart;
                if (commentsPart?.Comments != null)
                {
                    try
                    {
                        content.AppendLine("\n[COMMENTS]");
                        var comments = commentsPart.Comments.Elements<DocumentFormat.OpenXml.Wordprocessing.Comment>().Take(50);
                        
                        foreach (var comment in comments)
                        {
                            var author = comment.Author?.Value ?? "Unknown";
                            var commentText = comment.InnerText?.Trim();
                            if (!string.IsNullOrWhiteSpace(commentText))
                            {
                                content.AppendLine($"  {author}: {commentText}");
                            }
                        }
                        content.AppendLine("[END COMMENTS]");
                    }
                    catch (Exception ex)
                    {
                        content.AppendLine($"[COMMENTS ERROR] Failed to extract comments: {ex.Message}");
                    }
                }
                
                if (totalParagraphs > maxParagraphs)
                {
                    content.AppendLine($"\n[TRUNCATED] Remaining {totalParagraphs - maxParagraphs} paragraphs not processed due to size limits.");
                }

                return content.ToString();
            }
            catch (Exception ex)
            {
                return $"[DOCX ERROR] Failed to process Word document: {ex.Message}";
            }
        }

        private static string ExtractTextFromXlsx(string filePath)
        {
            try
            {
                // Enhanced Excel extraction with comprehensive cell handling
                using var spreadsheet = SpreadsheetDocument.Open(filePath, false);
                var content = new StringBuilder();

                var workbookPart = spreadsheet.WorkbookPart;
                if (workbookPart == null) return "[EXCEL ERROR] Invalid Excel file structure.";

                var sheets = workbookPart.Workbook.Descendants<Sheet>().ToList();
                const int maxSheets = 50; // Limit sheets to prevent memory issues
                const int maxRows = 10000; // Limit rows per sheet
                const int maxCols = 500; // Limit columns per sheet

                int sheetsToProcess = Math.Min(sheets.Count, maxSheets);
                if (sheets.Count > maxSheets)
                {
                    content.AppendLine($"[LARGE EXCEL WARNING] Excel has {sheets.Count} sheets. Processing first {maxSheets} sheets only.\n");
                }

                int sheetCount = 0;
                foreach (var sheet in sheets.Take(sheetsToProcess))
                {
                    try
                    {
                        var sheetName = sheet.Name?.Value ?? $"Sheet{sheetCount + 1}";
                        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id);
                        var worksheet = worksheetPart.Worksheet;

                        content.AppendLine($"[SHEET {sheetName}]");

                        // Get all cells with their addresses to maintain proper grid structure
                        var allCells = worksheet.Descendants<Cell>().ToList();
                        var cellDict = new Dictionary<string, Cell>();

                        foreach (var cell in allCells)
                        {
                            if (cell.CellReference != null)
                            {
                                cellDict[cell.CellReference.Value] = cell;
                            }
                        }

                        // Find the range of data with limits
                        if (cellDict.Any())
                        {
                            var minRow = cellDict.Keys.Min(cellRef => GetRowIndex(cellRef));
                            var maxRow = Math.Min(cellDict.Keys.Max(cellRef => GetRowIndex(cellRef)), minRow + maxRows);
                            var minCol = cellDict.Keys.Min(cellRef => GetColumnIndex(cellRef));
                            var maxCol = Math.Min(cellDict.Keys.Max(cellRef => GetColumnIndex(cellRef)), minCol + maxCols);

                            bool rowLimitReached = cellDict.Keys.Max(cellRef => GetRowIndex(cellRef)) > maxRow;
                            bool colLimitReached = cellDict.Keys.Max(cellRef => GetColumnIndex(cellRef)) > maxCol;

                            if (rowLimitReached || colLimitReached)
                            {
                                content.AppendLine($"[LARGE SHEET WARNING] Sheet truncated - processing {maxRow - minRow + 1} rows and {maxCol - minCol + 1} columns max.\n");
                            }

                            for (int row = minRow; row <= maxRow; row++)
                            {
                                content.AppendLine($"[ROW {row}]");
                                var rowCells = new List<string>();

                                for (int col = minCol; col <= maxCol; col++)
                                {
                                    var cellRef = GetCellReference(row, col);
                                    if (cellDict.ContainsKey(cellRef))
                                    {
                                        var cellValue = GetCellValue(cellDict[cellRef], workbookPart);
                                        rowCells.Add(cellValue);
                                    }
                                    else
                                    {
                                        rowCells.Add(""); // Empty cell
                                    }
                                }

                                // Only add rows that have some content
                                if (rowCells.Any(c => !string.IsNullOrWhiteSpace(c)))
                                {
                                    content.AppendLine(string.Join(" | ", rowCells));
                                }

                                content.AppendLine($"[END ROW {row}]");
                                
                                // Garbage collection for very large sheets
                                if ((row - minRow) % 1000 == 0)
                                {
                                    GC.Collect(0, GCCollectionMode.Optimized);
                                }
                            }
                        }

                        // Extract comments and hyperlinks (limited to prevent overflow)
                        var comments = worksheetPart.WorksheetCommentsPart?.Comments?.CommentList?.Elements<DocumentFormat.OpenXml.Spreadsheet.Comment>().Take(100);
                        if (comments?.Any() == true)
                        {
                            content.AppendLine("[COMMENTS]");
                            foreach (var comment in comments)
                            {
                                content.AppendLine($"Cell {comment.Reference}: {comment.CommentText?.InnerText}");
                            }
                            content.AppendLine("[END COMMENTS]");
                        }

                        var hyperlinks = worksheet.Descendants<Hyperlink>().Take(100);
                        if (hyperlinks?.Any() == true)
                        {
                            content.AppendLine("[HYPERLINKS]");
                            foreach (var hyperlink in hyperlinks)
                            {
                                var linkText = hyperlink.Reference?.Value ?? "Unknown";
                                var linkTarget = hyperlink.Id?.Value ?? hyperlink.Location?.Value ?? "Unknown";
                                content.AppendLine($"Cell {linkText}: {linkTarget}");
                            }
                            content.AppendLine("[END HYPERLINKS]");
                        }

                        content.AppendLine($"[END SHEET {sheetName}]");
                        sheetCount++;
                    }
                    catch (Exception ex)
                    {
                        content.AppendLine($"[SHEET ERROR] Failed to process sheet: {ex.Message}");
                    }
                }

                return content.ToString();
            }
            catch (Exception ex)
            {
                return $"[EXCEL ERROR] Failed to process Excel file: {ex.Message}";
            }
        }

        private static int GetRowIndex(string cellReference)
        {
            var match = System.Text.RegularExpressions.Regex.Match(cellReference, @"\d+");
            return match.Success ? int.Parse(match.Value) : 1;
        }

        private static int GetColumnIndex(string cellReference)
        {
            var match = System.Text.RegularExpressions.Regex.Match(cellReference, @"[A-Z]+");
            if (!match.Success) return 1;

            var columnName = match.Value;
            int result = 0;
            for (int i = 0; i < columnName.Length; i++)
            {
                result = result * 26 + (columnName[i] - 'A' + 1);
            }
            return result;
        }

        private static string GetCellReference(int row, int col)
        {
            string columnName = "";
            while (col > 0)
            {
                col--;
                columnName = (char)('A' + col % 26) + columnName;
                col /= 26;
            }
            return columnName + row;
        }

        public static Dictionary<string, List<Dictionary<string, string>>> ExtractStructuredDataFromXlsx(string filePath)
        {
            var result = new Dictionary<string, List<Dictionary<string, string>>>();

            using var spreadsheet = SpreadsheetDocument.Open(filePath, false);
            var workbookPart = spreadsheet.WorkbookPart;
            if (workbookPart == null) return result;

            var sheets = workbookPart.Workbook.Descendants<Sheet>().ToList();

            foreach (var sheet in sheets)
            {
                var sheetName = sheet.Name?.Value ?? "Sheet1";
                var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id);
                var worksheet = worksheetPart.Worksheet;

                var rows = worksheet.Descendants<Row>().ToList();
                if (!rows.Any()) continue;

                // Get headers from first row
                var headerRow = rows.First();
                var headers = new List<string>();

                foreach (var cell in headerRow.Descendants<Cell>())
                {
                    var cellValue = GetCellValue(cell, workbookPart);
                    headers.Add(cellValue ?? $"Column{headers.Count + 1}");
                }

                if (!headers.Any()) continue;

                // Process data rows
                var sheetData = new List<Dictionary<string, string>>();

                foreach (var row in rows.Skip(1)) // Skip header row
                {
                    var rowData = new Dictionary<string, string>();
                    var cells = row.Descendants<Cell>().ToList();

                    for (int i = 0; i < headers.Count; i++)
                    {
                        var cellValue = "";
                        if (i < cells.Count)
                        {
                            cellValue = GetCellValue(cells[i], workbookPart) ?? "";
                        }
                        rowData[headers[i]] = cellValue;
                    }

                    // Only add non-empty rows
                    if (rowData.Values.Any(v => !string.IsNullOrWhiteSpace(v)))
                    {
                        sheetData.Add(rowData);
                    }
                }

                result[sheetName] = sheetData;
            }

            return result;
        }

        private static string GetCellValue(Cell cell, WorkbookPart workbookPart)
        {
            if (cell?.CellValue == null) return "";

            var value = cell.CellValue.Text;

            // Handle shared strings
            if (cell.DataType != null && cell.DataType == CellValues.SharedString)
            {
                var sharedStringTable = workbookPart.SharedStringTablePart?.SharedStringTable;
                if (sharedStringTable != null && int.TryParse(value, out int index))
                {
                    var sharedString = sharedStringTable.Elements<SharedStringItem>().ElementAtOrDefault(index);
                    return sharedString?.InnerText ?? value;
                }
            }
            // Handle formulas - get the calculated value
            else if (cell.DataType != null && cell.DataType == CellValues.String)
            {
                return value;
            }
            // Handle inline strings
            else if (cell.DataType == null && cell.InlineString != null)
            {
                return cell.InlineString.InnerText;
            }
            // Handle boolean values
            else if (cell.DataType != null && cell.DataType == CellValues.Boolean)
            {
                return value == "1" ? "TRUE" : "FALSE";
            }
            // Handle dates and numbers
            else if (cell.DataType == null || cell.DataType == CellValues.Number)
            {
                // Check if it's a date by looking at the style
                if (double.TryParse(value, out double numValue))
                {
                    // Basic date detection - Excel dates are stored as numbers since 1900-01-01
                    if (numValue > 1 && numValue < 2958466) // Reasonable date range
                    {
                        try
                        {
                            var date = DateTime.FromOADate(numValue);
                            // If it looks like a date (not just a time), format it
                            if (date.Date != DateTime.MinValue.Date)
                            {
                                return date.ToString("yyyy-MM-dd HH:mm:ss");
                            }
                        }
                        catch
                        {
                            // If date conversion fails, return as number
                        }
                    }
                }
                return value;
            }

            return value ?? "";
        }

        public static HighResolutionDocument ExtractTextFromXlsxHighRes(string filePath)
        {
            var structuredData = ExtractStructuredDataFromXlsx(filePath);
            var content = new StringBuilder();
            var contentBlocks = new List<ContentBlock>();
            int globalPosition = 0;

            foreach (var sheet in structuredData)
            {
                var sheetName = sheet.Key;
                var rows = sheet.Value;

                content.AppendLine($"[SHEET {sheetName}]");
                globalPosition += $"[SHEET {sheetName}]".Length + 2;

                for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
                {
                    var row = rows[rowIndex];
                    var rowHeader = $"[ROW {rowIndex + 2}]"; // +2 because we skip header and are 1-indexed
                    var rowContent = new StringBuilder();

                    foreach (var cell in row)
                    {
                        rowContent.AppendLine($"{cell.Key}: {cell.Value}");
                    }

                    // Add row block
                    var rowBlock = new ContentBlock
                    {
                        Content = rowContent.ToString(),
                        StartPosition = globalPosition + rowHeader.Length + 2,
                        EndPosition = globalPosition + rowHeader.Length + rowContent.Length + 1,
                        Type = ContentBlockType.ExcelRow,
                        Properties = new Dictionary<string, string>
                        {
                            ["sheet_name"] = sheetName,
                            ["row_number"] = (rowIndex + 2).ToString(),
                            ["cell_count"] = row.Count.ToString()
                        }
                    };

                    contentBlocks.Add(rowBlock);

                    content.AppendLine(rowHeader);
                    content.AppendLine(rowContent.ToString());
                    content.AppendLine($"[END ROW {rowIndex + 2}]");

                    globalPosition = content.Length;
                }

                content.AppendLine($"[END SHEET {sheetName}]");
                globalPosition += $"[END SHEET {sheetName}]".Length + 2;
            }

            var analyzer = new HighResolutionTextAnalyzer();
            var document = analyzer.AnalyzeDocument(
                content.ToString(),
                filePath,
                Path.GetFileName(filePath),
                "xlsx"
            );

            // Add Excel-specific content blocks
            document.ContentBlocks.AddRange(contentBlocks);

            return document;
        }

        private static string ExtractTextFromPptx(string filePath)
        {
            try
            {
                // Comprehensive PowerPoint extraction with slide structure
                using var presentation = PresentationDocument.Open(filePath, false);
                var content = new StringBuilder();

                var slideParts = presentation.PresentationPart?.SlideParts?.ToList() ?? new List<SlidePart>();
                
                // Limit slides for very large presentations to prevent memory issues
                const int maxSlides = 500;
                const int maxTablesPerSlide = 20;
                const int maxCommentsPerSlide = 50;
                
                int totalSlides = slideParts.Count;
                int slidesToProcess = Math.Min(totalSlides, maxSlides);
                
                if (totalSlides > maxSlides)
                {
                    content.AppendLine($"[LARGE PPTX WARNING] PowerPoint has {totalSlides} slides. Processing first {maxSlides} slides only.\n");
                }

                for (int slideIndex = 0; slideIndex < slidesToProcess; slideIndex++)
                {
                    try
                    {
                        var slidePart = slideParts[slideIndex];
                        content.AppendLine($"[SLIDE {slideIndex + 1}]");

                        // Extract text from all shapes in the slide
                        var slide = slidePart.Slide;
                        if (slide?.CommonSlideData?.ShapeTree != null)
                        {
                            foreach (var shape in slide.CommonSlideData.ShapeTree.Elements())
                            {
                                try
                                {
                                    // Extract text from text bodies
                                    var textBodies = shape.Descendants<DocumentFormat.OpenXml.Drawing.Text>();
                                    foreach (var textElement in textBodies)
                                    {
                                        if (!string.IsNullOrWhiteSpace(textElement.Text))
                                        {
                                            content.AppendLine($"  {textElement.Text.Trim()}");
                                        }
                                    }

                                    // Extract hyperlink text
                                    var hyperlinks = shape.Descendants<DocumentFormat.OpenXml.Drawing.HyperlinkType>();
                                    foreach (var hyperlink in hyperlinks)
                                    {
                                        if (!string.IsNullOrWhiteSpace(hyperlink.InnerText))
                                        {
                                            content.AppendLine($"  [LINK] {hyperlink.InnerText.Trim()}");
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    content.AppendLine($"  [SHAPE ERROR] Failed to extract shape content: {ex.Message}");
                                }
                            }
                        }

                        // Extract slide notes if available
                        var notesSlidePart = slidePart.NotesSlidePart;
                        if (notesSlidePart?.NotesSlide != null)
                        {
                            try
                            {
                                content.AppendLine("  [NOTES]");
                                var notesText = notesSlidePart.NotesSlide.InnerText;
                                if (!string.IsNullOrWhiteSpace(notesText))
                                {
                                    // Clean up the notes text (remove template text)
                                    var lines = notesText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                                    foreach (var line in lines.Take(100)) // Limit notes lines
                                    {
                                        var cleanLine = line.Trim();
                                        // Skip common template phrases
                                        if (!string.IsNullOrEmpty(cleanLine) &&
                                            !cleanLine.Contains("Click to add notes") &&
                                            !cleanLine.Contains("Notes placeholder"))
                                        {
                                            content.AppendLine($"    {cleanLine}");
                                        }
                                    }
                                }
                                content.AppendLine("  [END NOTES]");
                            }
                            catch (Exception ex)
                            {
                                content.AppendLine($"  [NOTES ERROR] Failed to extract notes: {ex.Message}");
                            }
                        }

                        // Extract comments if available (limited to prevent overflow)
                        var commentsPart = slidePart.SlideCommentsPart;
                        if (commentsPart?.CommentList != null)
                        {
                            try
                            {
                                content.AppendLine("  [COMMENTS]");
                                var comments = commentsPart.CommentList.Elements<DocumentFormat.OpenXml.Presentation.Comment>().Take(maxCommentsPerSlide);
                                foreach (var comment in comments)
                                {
                                    var commentText = comment.Text?.InnerText;
                                    var author = comment.AuthorId?.Value;
                                    if (!string.IsNullOrWhiteSpace(commentText))
                                    {
                                        content.AppendLine($"    {author}: {commentText.Trim()}");
                                    }
                                }
                                content.AppendLine("  [END COMMENTS]");
                            }
                            catch (Exception ex)
                            {
                                content.AppendLine($"  [COMMENTS ERROR] Failed to extract comments: {ex.Message}");
                            }
                        }

                        // Extract table data if present (limited to prevent overflow)
                        var tables = slide.Descendants<DocumentFormat.OpenXml.Drawing.Table>().Take(maxTablesPerSlide);
                        foreach (var table in tables)
                        {
                            try
                            {
                                content.AppendLine("  [TABLE]");
                                var rows = table.Elements<DocumentFormat.OpenXml.Drawing.TableRow>().Take(50); // Limit rows per table
                                foreach (var row in rows)
                                {
                                    var cells = row.Elements<DocumentFormat.OpenXml.Drawing.TableCell>().Take(20); // Limit columns per row
                                    var cellTexts = cells.Select(cell => cell.InnerText.Trim()).Where(text => !string.IsNullOrWhiteSpace(text));
                                    if (cellTexts.Any())
                                    {
                                        content.AppendLine($"    {string.Join(" | ", cellTexts)}");
                                    }
                                }
                                content.AppendLine("  [END TABLE]");
                            }
                            catch (Exception ex)
                            {
                                content.AppendLine($"  [TABLE ERROR] Failed to extract table: {ex.Message}");
                            }
                        }

                        content.AppendLine($"[END SLIDE {slideIndex + 1}]");
                        content.AppendLine();
                        
                        // Force garbage collection every 25 slides for large presentations
                        if ((slideIndex + 1) % 25 == 0)
                        {
                            GC.Collect(0, GCCollectionMode.Optimized);
                        }
                    }
                    catch (Exception ex)
                    {
                        content.AppendLine($"[SLIDE ERROR {slideIndex + 1}] Failed to process slide: {ex.Message}");
                    }
                }

                // Extract master slide content if present (limited)
                var slideMasterParts = presentation.PresentationPart?.SlideMasterParts?.Take(10); // Limit master slides
                if (slideMasterParts?.Any() == true)
                {
                    try
                    {
                        content.AppendLine("[MASTER SLIDES]");
                        foreach (var masterPart in slideMasterParts)
                        {
                            var masterText = masterPart.SlideMaster?.InnerText;
                            if (!string.IsNullOrWhiteSpace(masterText))
                            {
                                // Extract only relevant content, skip template placeholders
                                var lines = masterText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                                foreach (var line in lines.Take(100)) // Limit master slide lines
                                {
                                    var cleanLine = line.Trim();
                                    if (!string.IsNullOrEmpty(cleanLine) &&
                                        !cleanLine.Contains("Click to edit") &&
                                        !cleanLine.Contains("Master title") &&
                                        !cleanLine.Contains("Master text"))
                                    {
                                        content.AppendLine($"  {cleanLine}");
                                    }
                                }
                            }
                        }
                        content.AppendLine("[END MASTER SLIDES]");
                    }
                    catch (Exception ex)
                    {
                        content.AppendLine($"[MASTER SLIDES ERROR] Failed to extract master slides: {ex.Message}");
                    }
                }
                
                if (totalSlides > maxSlides)
                {
                    content.AppendLine($"\n[TRUNCATED] Remaining {totalSlides - maxSlides} slides not processed due to size limits.");
                }

                return content.ToString();
            }
            catch (Exception ex)
            {
                return $"[PPTX ERROR] Failed to process PowerPoint file: {ex.Message}";
            }
        }



    }
}
