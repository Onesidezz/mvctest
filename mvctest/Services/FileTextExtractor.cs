using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Presentation;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using TRIM.SDK;
using mvctest.Models;
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
                ".xlsx" => ExtractTextFromXlsx(filePath), // Requires more handling for sheets
                ".pptx" => ExtractTextFromPptx(filePath), // Extracts slide text
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
            // Enhanced Excel extraction with row/column structure
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
                
                var rows = worksheet.Descendants<Row>().ToList();
                for (int i = 0; i < rows.Count; i++)
                {
                    content.AppendLine($"[ROW {i + 1}]");
                    var cells = rows[i].Descendants<Cell>().ToList();
                    
                    foreach (var cell in cells)
                    {
                        var cellValue = GetCellValue(cell, workbookPart);
                        if (!string.IsNullOrEmpty(cellValue))
                        {
                            content.Append(cellValue + " ");
                        }
                    }
                    content.AppendLine();
                    content.AppendLine($"[END ROW {i + 1}]");
                }
                
                content.AppendLine($"[END SHEET {sheetName}]");
            }
            
            return content.ToString();
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
            if (cell?.CellValue == null) return null;
            
            var value = cell.CellValue.Text;
            
            if (cell.DataType != null && cell.DataType == CellValues.SharedString)
            {
                var sharedStringTable = workbookPart.SharedStringTablePart?.SharedStringTable;
                if (sharedStringTable != null && int.TryParse(value, out int index))
                {
                    var sharedString = sharedStringTable.Elements<SharedStringItem>().ElementAtOrDefault(index);
                    return sharedString?.InnerText ?? value;
                }
            }
            
            return value;
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
            // Extracts text from all slides
            using var presentation = PresentationDocument.Open(filePath, false);
            var text = new StringBuilder();
            foreach (var slide in presentation.PresentationPart?.SlideParts ?? Enumerable.Empty<SlidePart>())
            {
                text.Append(slide.Slide.InnerText);
            }
            return text.ToString();
        }



    }
}
