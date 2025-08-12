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
            string extension = Path.GetExtension(filePath).ToLower();

            return extension switch
            {
                ".txt" => File.ReadAllText(filePath),
                ".pdf" => ExtractTextFromPdf(filePath),
                ".docx" => ExtractTextFromDocx(filePath),
                ".xlsx" => ExtractTextFromXlsx(filePath),
                ".pptx" => ExtractTextFromPptx(filePath),
                _ => throw new NotSupportedException($"File type '{extension}' is not supported."),
            };
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
            using var pdfReader = new PdfReader(filePath);
            using var pdfDoc = new PdfDocument(pdfReader);

            var text = new StringBuilder();
            for (int pageNum = 1; pageNum <= pdfDoc.GetNumberOfPages(); pageNum++)
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
            }
            return text.ToString();
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
            using var doc = WordprocessingDocument.Open(filePath, false);
            return doc.MainDocumentPart?.Document.Body?.InnerText ?? string.Empty;
        }

        private static string ExtractTextFromXlsx(string filePath)
        {
            // Enhanced Excel extraction with comprehensive cell handling
            using var spreadsheet = SpreadsheetDocument.Open(filePath, false);
            var content = new StringBuilder();

            var workbookPart = spreadsheet.WorkbookPart;
            if (workbookPart == null) return "";

            var sheets = workbookPart.Workbook.Descendants<Sheet>().ToList();

            foreach (var sheet in sheets)
            {
                var sheetName = sheet.Name?.Value ?? "Sheet1";
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

                // Find the range of data
                if (cellDict.Any())
                {
                    var minRow = cellDict.Keys.Min(cellRef => GetRowIndex(cellRef));
                    var maxRow = cellDict.Keys.Max(cellRef => GetRowIndex(cellRef));
                    var minCol = cellDict.Keys.Min(cellRef => GetColumnIndex(cellRef));
                    var maxCol = cellDict.Keys.Max(cellRef => GetColumnIndex(cellRef));

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
                    }
                }

                // Also extract comments and hyperlinks
                var comments = worksheetPart.WorksheetCommentsPart?.Comments?.CommentList?.Elements<DocumentFormat.OpenXml.Spreadsheet.Comment>();
                if (comments?.Any() == true)
                {
                    content.AppendLine("[COMMENTS]");
                    foreach (var comment in comments)
                    {
                        content.AppendLine($"Cell {comment.Reference}: {comment.CommentText?.InnerText}");
                    }
                    content.AppendLine("[END COMMENTS]");
                }

                var hyperlinks = worksheet.Descendants<Hyperlink>();
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
            }

            return content.ToString();
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
            // Comprehensive PowerPoint extraction with slide structure
            using var presentation = PresentationDocument.Open(filePath, false);
            var content = new StringBuilder();

            var slideParts = presentation.PresentationPart?.SlideParts?.ToList() ?? new List<SlidePart>();

            for (int slideIndex = 0; slideIndex < slideParts.Count; slideIndex++)
            {
                var slidePart = slideParts[slideIndex];
                content.AppendLine($"[SLIDE {slideIndex + 1}]");

                // Extract text from all shapes in the slide
                var slide = slidePart.Slide;
                if (slide?.CommonSlideData?.ShapeTree != null)
                {
                    foreach (var shape in slide.CommonSlideData.ShapeTree.Elements())
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
                }

                // Extract slide notes if available
                var notesSlidePart = slidePart.NotesSlidePart;
                if (notesSlidePart?.NotesSlide != null)
                {
                    content.AppendLine("  [NOTES]");
                    var notesText = notesSlidePart.NotesSlide.InnerText;
                    if (!string.IsNullOrWhiteSpace(notesText))
                    {
                        // Clean up the notes text (remove template text)
                        var lines = notesText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
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

                // Extract comments if available
                var commentsPart = slidePart.SlideCommentsPart;
                if (commentsPart?.CommentList != null)
                {
                    content.AppendLine("  [COMMENTS]");
                    foreach (var comment in commentsPart.CommentList.Elements<DocumentFormat.OpenXml.Presentation.Comment>())
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

                // Extract table data if present
                var tables = slide.Descendants<DocumentFormat.OpenXml.Drawing.Table>();
                foreach (var table in tables)
                {
                    content.AppendLine("  [TABLE]");
                    var rows = table.Elements<DocumentFormat.OpenXml.Drawing.TableRow>();
                    foreach (var row in rows)
                    {
                        var cells = row.Elements<DocumentFormat.OpenXml.Drawing.TableCell>();
                        var cellTexts = cells.Select(cell => cell.InnerText.Trim()).Where(text => !string.IsNullOrWhiteSpace(text));
                        if (cellTexts.Any())
                        {
                            content.AppendLine($"    {string.Join(" | ", cellTexts)}");
                        }
                    }
                    content.AppendLine("  [END TABLE]");
                }

                content.AppendLine($"[END SLIDE {slideIndex + 1}]");
                content.AppendLine();
            }

            // Extract master slide content if present
            var slideMasterParts = presentation.PresentationPart?.SlideMasterParts;
            if (slideMasterParts?.Any() == true)
            {
                content.AppendLine("[MASTER SLIDES]");
                foreach (var masterPart in slideMasterParts)
                {
                    var masterText = masterPart.SlideMaster?.InnerText;
                    if (!string.IsNullOrWhiteSpace(masterText))
                    {
                        // Extract only relevant content, skip template placeholders
                        var lines = masterText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
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

            return content.ToString();
        }



    }
}
