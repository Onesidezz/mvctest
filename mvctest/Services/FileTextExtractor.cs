namespace mvctest.Services
{
    using DocumentFormat.OpenXml.Packaging;
    using iText.Kernel.Pdf;
    using iText.Kernel.Pdf.Canvas.Parser;
    using System.IO;
    using System.Text;

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

        private static string ExtractTextFromPdf(string filePath)
        {
            using var pdfReader = new PdfReader(filePath);
            using var pdfDoc = new PdfDocument(pdfReader);

            var text = new StringBuilder();
            for (int page = 1; page <= pdfDoc.GetNumberOfPages(); page++)
            {
                text.Append(PdfTextExtractor.GetTextFromPage(pdfDoc.GetPage(page)));
            }
            return text.ToString();
        }

        private static string ExtractTextFromDocx(string filePath)
        {
            using var doc = WordprocessingDocument.Open(filePath, false);
            return doc.MainDocumentPart?.Document.Body?.InnerText ?? string.Empty;
        }

        private static string ExtractTextFromXlsx(string filePath)
        {
            // Simplified: Extracts text from the first sheet
            using var spreadsheet = SpreadsheetDocument.Open(filePath, false);
            var sheet = spreadsheet.WorkbookPart?.WorksheetParts.First()?.Worksheet;
            return sheet?.InnerText ?? string.Empty;
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
