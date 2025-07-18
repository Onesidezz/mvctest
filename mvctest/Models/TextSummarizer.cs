namespace mvctest.Models
{
    using mvctest.Services;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;

    public class TextSummarizer
    {
        private readonly HashSet<string> _stopWords = new HashSet<string>
    {
        "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for", "of", "with", "by",
        "this", "that", "these", "those", "is", "are", "was", "were", "be", "been", "being",
        "have", "has", "had", "do", "does", "did", "will", "would", "could", "should", "may",
        "might", "must", "can", "shall", "about", "into", "through", "during", "before",
        "after", "above", "below", "up", "down", "out", "off", "over", "under", "again",
        "further", "then", "once", "here", "there", "when", "where", "why", "how", "all",
        "any", "both", "each", "few", "more", "most", "other", "some", "such", "no", "nor",
        "not", "only", "own", "same", "so", "than", "too", "very", "just", "now"
    };

        public async Task<string> SummarizeTextAsync(string text, int maxSentences = 3)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "No content to summarize.";

            try
            {
                // Clean and preprocess text
                string cleanText = CleanText(text);

                // Split into sentences
                var sentences = SplitIntoSentences(cleanText);

                if (sentences.Count <= maxSentences)
                    return string.Join(" ", sentences);

                // Score sentences based on importance
                var sentenceScores = ScoreSentences(sentences, cleanText);

                // Get top sentences
                var topSentences = sentenceScores
                    .OrderByDescending(kvp => kvp.Value)
                    .Take(maxSentences)
                    .OrderBy(kvp => sentences.IndexOf(kvp.Key))
                    .Select(kvp => kvp.Key)
                    .ToList();

                return string.Join(" ", topSentences);
            }
            catch (Exception ex)
            {
                return $"Error generating summary: {ex.Message}";
            }
        }

        public async Task<string> SummarizeFileAsync(string filePath, int maxSentences = 3)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("File not found!");

            try
            {
                string fileContent = FileTextExtractor.ExtractTextFromFile(filePath);

                // Handle large files by taking a representative sample
                if (fileContent.Length > 20000)
                {
                    fileContent = GetRepresentativeSample(fileContent, 15000);
                }

                return await SummarizeTextAsync(fileContent, maxSentences);
            }
            catch (Exception ex)
            {
                return $"Error processing file: {ex.Message}";
            }
        }

        private string CleanText(string text)
        {
            // Remove excessive whitespace and normalize
            text = Regex.Replace(text, @"\s+", " ");
            text = Regex.Replace(text, @"[^\w\s\.\!\?\,\;\:\-\(\)]", "");
            return text.Trim();
        }

        private List<string> SplitIntoSentences(string text)
        {
            // Split on sentence endings but handle common abbreviations
            var sentences = Regex.Split(text, @"(?<=[.!?])\s+(?=[A-Z])")
                .Where(s => !string.IsNullOrWhiteSpace(s) && s.Length > 10)
                .Select(s => s.Trim())
                .ToList();

            return sentences;
        }

        private Dictionary<string, double> ScoreSentences(List<string> sentences, string fullText)
        {
            var sentenceScores = new Dictionary<string, double>();
            var wordFrequency = GetWordFrequency(fullText);

            foreach (var sentence in sentences)
            {
                double score = 0;
                var words = GetWords(sentence);

                if (words.Count == 0) continue;

                // Score based on word frequency
                foreach (var word in words)
                {
                    if (wordFrequency.ContainsKey(word))
                    {
                        score += wordFrequency[word];
                    }
                }

                // Normalize by sentence length
                score = score / words.Count;

                // Boost score for sentences with important indicators
                if (ContainsImportantKeywords(sentence))
                    score *= 1.5;

                // Penalize very short or very long sentences
                if (sentence.Length < 20 || sentence.Length > 200)
                    score *= 0.8;

                sentenceScores[sentence] = score;
            }

            return sentenceScores;
        }

        private Dictionary<string, double> GetWordFrequency(string text)
        {
            var words = GetWords(text);
            var frequency = new Dictionary<string, double>();

            foreach (var word in words)
            {
                frequency[word] = frequency.GetValueOrDefault(word, 0) + 1;
            }

            // Normalize frequencies
            var maxFreq = frequency.Values.DefaultIfEmpty(0).Max();
            if (maxFreq > 0)
            {
                var normalizedFreq = frequency.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value / maxFreq
                );
                return normalizedFreq;
            }

            return frequency;
        }

        private List<string> GetWords(string text)
        {
            return Regex.Matches(text.ToLower(), @"\b\w+\b")
                .Cast<Match>()
                .Select(m => m.Value)
                .Where(word => word.Length > 2 && !_stopWords.Contains(word))
                .ToList();
        }

        private bool ContainsImportantKeywords(string sentence)
        {
            var importantKeywords = new[] {
            // Importance indicators
            "important", "significant", "main", "primary", "key", "essential", "vital", "crucial",
            "critical", "fundamental", "major", "principal", "central", "core", "basic",
            
            // Summary/conclusion words
            "conclusion", "result", "summary", "overview", "findings", "outcome", "end result",
            "final", "ultimately", "in summary", "to summarize", "in conclusion", "overall",
            
            // Transition/emphasis words
            "therefore", "however", "moreover", "furthermore", "additionally", "consequently",
            "thus", "hence", "accordingly", "nevertheless", "nonetheless", "meanwhile",
            "similarly", "likewise", "conversely", "alternatively", "specifically", "notably",
            "particularly", "especially", "indeed", "certainly", "clearly", "obviously",
            
            // Problem/solution indicators
            "problem", "issue", "challenge", "difficulty", "solution", "answer", "approach",
            "method", "strategy", "technique", "process", "procedure", "step", "phase",
            
            // Analysis/evaluation words
            "analysis", "evaluation", "assessment", "examination", "review", "study", "research",
            "investigation", "survey", "report", "data", "evidence", "proof", "demonstrate",
            "show", "reveal", "indicate", "suggest", "imply", "prove", "establish",
            
            // Time/sequence indicators
            "first", "second", "third", "initially", "subsequently", "finally", "lastly",
            "next", "then", "after", "before", "during", "while", "meanwhile", "simultaneously",
            
            // Comparison/contrast
            "compared to", "in contrast", "unlike", "whereas", "while", "although", "despite",
            "instead", "rather than", "on the other hand", "in comparison", "similarly",
            
            // Emphasis/strong statements
            "must", "should", "need to", "required", "necessary", "mandatory", "essential",
            "recommended", "suggested", "advised", "proposed", "urgent", "immediate",
            
            // Quantitative/statistical
            "percent", "percentage", "ratio", "proportion", "majority", "minority", "most",
            "least", "average", "typical", "common", "rare", "frequent", "increase", "decrease",
            
            // Business/technical terms
            "objective", "goal", "target", "milestone", "achievement", "success", "failure",
            "implementation", "development", "improvement", "optimization", "efficiency",
            "performance", "quality", "standard", "requirement", "specification", "feature",
            
            // Action/directive words
            "action", "implement", "execute", "perform", "conduct", "carry out", "complete",
            "achieve", "accomplish", "deliver", "provide", "ensure", "maintain", "establish",
            
            // Descriptive intensity
            "extremely", "highly", "very", "quite", "rather", "somewhat", "moderately",
            "significantly", "substantially", "considerably", "remarkably", "exceptionally"
        };

            return importantKeywords.Any(keyword =>
                sentence.ToLower().Contains(keyword));
        }

        private string GetRepresentativeSample(string text, int maxLength)
        {
            // Take beginning, middle, and end portions
            int segmentLength = maxLength / 3;
            var beginning = text.Substring(0, Math.Min(segmentLength, text.Length));

            if (text.Length <= segmentLength)
                return beginning;

            var middle = text.Substring(
                text.Length / 2 - segmentLength / 2,
                Math.Min(segmentLength, text.Length - text.Length / 2 + segmentLength / 2)
            );

            var end = text.Length > segmentLength * 2
                ? text.Substring(text.Length - segmentLength, segmentLength)
                : "";

            return $"{beginning}\n\n{middle}\n\n{end}";
        }
    }

   
 

    // Usage example
    //public class SummaryController
    //{
    //    private readonly LocalSummarizationService _summaryService;

    //    public SummaryController()
    //    {
    //        _summaryService = new LocalSummarizationService();
    //    }

    //    public async Task<string> ProcessSummaryRequest(string? filePath = null)
    //    {
    //        try
    //        {
    //            var summary = await _summaryService.GetSummaryAsync("", filePath);
    //            return summary;
    //        }
    //        catch (Exception ex)
    //        {
    //            return $"Summary generation failed: {ex.Message}";
    //        }
    //    }
    //}
}
