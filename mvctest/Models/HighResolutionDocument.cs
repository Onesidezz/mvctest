using System.Text.Json.Serialization;

namespace mvctest.Models
{
    public class HighResolutionDocument
    {
        public string FilePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public string FileType { get; set; } = "";
        public DateTime IndexedDate { get; set; }
        
        // Document-level metrics
        public int TotalCharacters { get; set; }
        public int TotalWords { get; set; }
        public int TotalSentences { get; set; }
        public int TotalParagraphs { get; set; }
        public int TotalLines { get; set; }
        
        // Content analysis
        public List<WordOccurrence> WordOccurrences { get; set; } = new();
        public List<CharacterSequence> CharacterSequences { get; set; } = new();
        public List<NGram> NGrams { get; set; } = new();
        public Dictionary<string, int> WordFrequency { get; set; } = new();
        public Dictionary<string, List<int>> WordPositions { get; set; } = new();
        
        // Metadata
        public dynamic? Metadata { get; set; }
        public Dictionary<string, string> CustomFields { get; set; } = new();
        
        // Full content preservation
        public string FullContent { get; set; } = "";
        public List<ContentBlock> ContentBlocks { get; set; } = new();
    }
    
    public class WordOccurrence
    {
        public string Word { get; set; } = "";
        public string NormalizedWord { get; set; } = "";
        public int StartPosition { get; set; }
        public int EndPosition { get; set; }
        public int LineNumber { get; set; }
        public int ColumnNumber { get; set; }
        public int SentenceNumber { get; set; }
        public int ParagraphNumber { get; set; }
        public string Context { get; set; } = ""; // Surrounding words
        public WordType Type { get; set; }
        public double Confidence { get; set; } = 1.0;
    }
    
    public class CharacterSequence
    {
        public char Character { get; set; }
        public int Position { get; set; }
        public int LineNumber { get; set; }
        public int ColumnNumber { get; set; }
        public CharacterType Type { get; set; }
        public string UnicodeCategory { get; set; } = "";
    }
    
    public class NGram
    {
        public string Text { get; set; } = "";
        public int N { get; set; } // 1=unigram, 2=bigram, 3=trigram, etc.
        public int StartPosition { get; set; }
        public int EndPosition { get; set; }
        public int Frequency { get; set; }
        public List<int> Positions { get; set; } = new();
    }
    
    public class ContentBlock
    {
        public string Content { get; set; } = "";
        public int StartPosition { get; set; }
        public int EndPosition { get; set; }
        public ContentBlockType Type { get; set; }
        public Dictionary<string, string> Properties { get; set; } = new();
        public int LineStart { get; set; }
        public int LineEnd { get; set; }
    }
    
    public enum WordType
    {
        Word,
        Number,
        Punctuation,
        Symbol,
        Whitespace,
        Email,
        Url,
        Date,
        Currency,
        Alphanumeric
    }
    
    public enum CharacterType
    {
        Letter,
        Digit,
        Punctuation,
        Symbol,
        Whitespace,
        Control,
        Other
    }
    
    public enum ContentBlockType
    {
        Paragraph,
        Sentence,
        Line,
        Table,
        List,
        Header,
        Footer,
        ExcelCell,
        ExcelRow,
        Slide
    }
}