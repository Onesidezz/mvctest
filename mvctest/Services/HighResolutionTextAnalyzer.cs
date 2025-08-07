using mvctest.Models;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace mvctest.Services
{
    public class HighResolutionTextAnalyzer
    {
        private readonly Regex _wordPattern;
        private readonly Regex _sentencePattern;
        private readonly Regex _paragraphPattern;
        private readonly Regex _emailPattern;
        private readonly Regex _urlPattern;
        private readonly Regex _datePattern;
        private readonly Regex _currencyPattern;
        
        public HighResolutionTextAnalyzer()
        {
            _wordPattern = new Regex(@"\b\w+\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            _sentencePattern = new Regex(@"[.!?]+", RegexOptions.Compiled);
            _paragraphPattern = new Regex(@"\n\s*\n", RegexOptions.Compiled);
            _emailPattern = new Regex(@"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b", RegexOptions.Compiled);
            _urlPattern = new Regex(@"https?://[^\s]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            _datePattern = new Regex(@"\b\d{1,2}[/-]\d{1,2}[/-]\d{2,4}\b|\b\d{4}[/-]\d{1,2}[/-]\d{1,2}\b", RegexOptions.Compiled);
            _currencyPattern = new Regex(@"[\$£€¥]\s*\d+(?:,\d{3})*(?:\.\d{2})?", RegexOptions.Compiled);
        }
        
        public HighResolutionDocument AnalyzeDocument(string content, string filePath, string fileName, string fileType, dynamic? metadata = null, Dictionary<string, string>? customFields = null)
        {
            var document = new HighResolutionDocument
            {
                FilePath = filePath,
                FileName = fileName,
                FileType = fileType,
                IndexedDate = DateTime.Now,
                FullContent = content,
                Metadata = metadata,
                CustomFields = customFields ?? new Dictionary<string, string>()
            };
            
            // Basic document metrics
            document.TotalCharacters = content.Length;
            document.TotalWords = CountWords(content);
            document.TotalSentences = CountSentences(content);
            document.TotalParagraphs = CountParagraphs(content);
            document.TotalLines = content.Split('\n').Length;
            
            // Detailed analysis
            AnalyzeCharacters(content, document);
            AnalyzeWords(content, document);
            GenerateNGrams(content, document);
            AnalyzeContentBlocks(content, document, fileType);
            
            return document;
        }
        
        private void AnalyzeCharacters(string content, HighResolutionDocument document)
        {
            var lines = content.Split('\n');
            int globalPosition = 0;
            
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var line = lines[lineIndex];
                for (int columnIndex = 0; columnIndex < line.Length; columnIndex++)
                {
                    var character = line[columnIndex];
                    var charSequence = new CharacterSequence
                    {
                        Character = character,
                        Position = globalPosition,
                        LineNumber = lineIndex + 1,
                        ColumnNumber = columnIndex + 1,
                        Type = GetCharacterType(character),
                        UnicodeCategory = CharUnicodeInfo.GetUnicodeCategory(character).ToString()
                    };
                    
                    document.CharacterSequences.Add(charSequence);
                    globalPosition++;
                }
                
                // Add newline character if not the last line
                if (lineIndex < lines.Length - 1)
                {
                    document.CharacterSequences.Add(new CharacterSequence
                    {
                        Character = '\n',
                        Position = globalPosition,
                        LineNumber = lineIndex + 1,
                        ColumnNumber = line.Length + 1,
                        Type = CharacterType.Control,
                        UnicodeCategory = CharUnicodeInfo.GetUnicodeCategory('\n').ToString()
                    });
                    globalPosition++;
                }
            }
        }
        
        private void AnalyzeWords(string content, HighResolutionDocument document)
        {
            var lines = content.Split('\n');
            var wordFrequency = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var wordPositions = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            
            int globalPosition = 0;
            int sentenceNumber = 1;
            int paragraphNumber = 1;
            
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var line = lines[lineIndex];
                
                // Check for paragraph break
                if (string.IsNullOrWhiteSpace(line) && lineIndex > 0)
                {
                    paragraphNumber++;
                }
                
                // Find all matches in the line
                var matches = _wordPattern.Matches(line);
                
                foreach (Match match in matches)
                {
                    var word = match.Value;
                    var normalizedWord = word.ToLowerInvariant();
                    var startPos = globalPosition + match.Index;
                    var endPos = startPos + word.Length - 1;
                    
                    // Get context (surrounding words)
                    var context = GetWordContext(content, startPos, 3);
                    
                    var occurrence = new WordOccurrence
                    {
                        Word = word,
                        NormalizedWord = normalizedWord,
                        StartPosition = startPos,
                        EndPosition = endPos,
                        LineNumber = lineIndex + 1,
                        ColumnNumber = match.Index + 1,
                        SentenceNumber = sentenceNumber,
                        ParagraphNumber = paragraphNumber,
                        Context = context,
                        Type = DetermineWordType(word),
                        Confidence = 1.0
                    };
                    
                    document.WordOccurrences.Add(occurrence);
                    
                    // Update frequency and positions
                    if (!wordFrequency.ContainsKey(normalizedWord))
                    {
                        wordFrequency[normalizedWord] = 0;
                        wordPositions[normalizedWord] = new List<int>();
                    }
                    
                    wordFrequency[normalizedWord]++;
                    wordPositions[normalizedWord].Add(startPos);
                }
                
                // Count sentences in the line
                var sentenceMatches = _sentencePattern.Matches(line);
                sentenceNumber += sentenceMatches.Count;
                
                globalPosition += line.Length + 1; // +1 for newline
            }
            
            document.WordFrequency = wordFrequency;
            document.WordPositions = wordPositions;
        }
        
        private void GenerateNGrams(string content, HighResolutionDocument document)
        {
            var words = _wordPattern.Matches(content).Cast<Match>().Select(m => m.Value.ToLowerInvariant()).ToList();
            
            // Generate n-grams from 1 to 5
            for (int n = 1; n <= 5; n++)
            {
                var ngramFrequency = new Dictionary<string, NGram>();
                
                for (int i = 0; i <= words.Count - n; i++)
                {
                    var ngram = string.Join(" ", words.Skip(i).Take(n));
                    
                    if (!ngramFrequency.ContainsKey(ngram))
                    {
                        ngramFrequency[ngram] = new NGram
                        {
                            Text = ngram,
                            N = n,
                            Frequency = 0,
                            Positions = new List<int>()
                        };
                    }
                    
                    ngramFrequency[ngram].Frequency++;
                    
                    // Find position in original text
                    var position = FindNGramPosition(content, words, i, n);
                    if (position >= 0)
                    {
                        ngramFrequency[ngram].Positions.Add(position);
                        if (ngramFrequency[ngram].StartPosition == 0)
                        {
                            ngramFrequency[ngram].StartPosition = position;
                        }
                    }
                }
                
                document.NGrams.AddRange(ngramFrequency.Values);
            }
        }
        
        private void AnalyzeContentBlocks(string content, HighResolutionDocument document, string fileType)
        {
            var lines = content.Split('\n');
            
            // Paragraph-level blocks
            var paragraphs = _paragraphPattern.Split(content);
            int paragraphStart = 0;
            
            foreach (var paragraph in paragraphs)
            {
                if (!string.IsNullOrWhiteSpace(paragraph))
                {
                    var block = new ContentBlock
                    {
                        Content = paragraph.Trim(),
                        StartPosition = paragraphStart,
                        EndPosition = paragraphStart + paragraph.Length - 1,
                        Type = ContentBlockType.Paragraph,
                        Properties = new Dictionary<string, string>
                        {
                            ["length"] = paragraph.Length.ToString(),
                            ["word_count"] = CountWords(paragraph).ToString()
                        }
                    };
                    
                    document.ContentBlocks.Add(block);
                }
                
                paragraphStart += paragraph.Length;
            }
            
            // Sentence-level blocks
            var sentences = _sentencePattern.Split(content);
            int sentenceStart = 0;
            
            foreach (var sentence in sentences)
            {
                if (!string.IsNullOrWhiteSpace(sentence))
                {
                    var block = new ContentBlock
                    {
                        Content = sentence.Trim(),
                        StartPosition = sentenceStart,
                        EndPosition = sentenceStart + sentence.Length - 1,
                        Type = ContentBlockType.Sentence,
                        Properties = new Dictionary<string, string>
                        {
                            ["length"] = sentence.Length.ToString(),
                            ["word_count"] = CountWords(sentence).ToString()
                        }
                    };
                    
                    document.ContentBlocks.Add(block);
                }
                
                sentenceStart += sentence.Length;
            }
        }
        
        private string GetWordContext(string content, int position, int contextSize)
        {
            var words = new List<string>();
            var matches = _wordPattern.Matches(content);
            
            foreach (Match match in matches)
            {
                if (Math.Abs(match.Index - position) <= contextSize * 10) // Approximate context window
                {
                    words.Add(match.Value);
                }
            }
            
            return string.Join(" ", words.Take(contextSize * 2 + 1));
        }
        
        private WordType DetermineWordType(string word)
        {
            if (_emailPattern.IsMatch(word)) return WordType.Email;
            if (_urlPattern.IsMatch(word)) return WordType.Url;
            if (_datePattern.IsMatch(word)) return WordType.Date;
            if (_currencyPattern.IsMatch(word)) return WordType.Currency;
            if (double.TryParse(word, out _)) return WordType.Number;
            if (word.Any(char.IsDigit) && word.Any(char.IsLetter)) return WordType.Alphanumeric;
            if (word.All(char.IsPunctuation)) return WordType.Punctuation;
            if (word.All(char.IsSymbol)) return WordType.Symbol;
            if (word.All(char.IsWhiteSpace)) return WordType.Whitespace;
            
            return WordType.Word;
        }
        
        private CharacterType GetCharacterType(char character)
        {
            if (char.IsLetter(character)) return CharacterType.Letter;
            if (char.IsDigit(character)) return CharacterType.Digit;
            if (char.IsPunctuation(character)) return CharacterType.Punctuation;
            if (char.IsSymbol(character)) return CharacterType.Symbol;
            if (char.IsWhiteSpace(character)) return CharacterType.Whitespace;
            if (char.IsControl(character)) return CharacterType.Control;
            
            return CharacterType.Other;
        }
        
        private int CountWords(string text)
        {
            return _wordPattern.Matches(text).Count;
        }
        
        private int CountSentences(string text)
        {
            return _sentencePattern.Matches(text).Count;
        }
        
        private int CountParagraphs(string text)
        {
            return _paragraphPattern.Split(text).Where(p => !string.IsNullOrWhiteSpace(p)).Count();
        }
        
        private int FindNGramPosition(string content, List<string> words, int wordIndex, int n)
        {
            if (wordIndex + n > words.Count) return -1;
            
            var ngramText = string.Join(" ", words.Skip(wordIndex).Take(n));
            return content.IndexOf(ngramText, StringComparison.OrdinalIgnoreCase);
        }
    }
}