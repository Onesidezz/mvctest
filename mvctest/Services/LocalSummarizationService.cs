using mvctest.Models;

namespace mvctest.Services
{
    public class LocalSummarizationService
    {
        private readonly TextSummarizer _summarizer;

        public LocalSummarizationService()
        {
            _summarizer = new TextSummarizer();
        }

        public async Task<string> GetSummaryAsync(string userInput, string? filePath = null)
        {
            try
            {
                if (!string.IsNullOrEmpty(filePath))
                {
                    // Generate file summary
                    var summary = await _summarizer.SummarizeFileAsync(filePath, maxSentences: 3);
                    return $"File Summary:\n{summary}";
                }
                else if (!string.IsNullOrEmpty(userInput))
                {
                    // Generate text summary
                    var summary = await _summarizer.SummarizeTextAsync(userInput, maxSentences: 3);
                    return $"Text Summary:\n{summary}";
                }
                else
                {
                    return "No content provided for summarization.";
                }
            }
            catch (Exception ex)
            {
                return $"Error generating summary: {ex.Message}";
            }
        }

        // Optional: Hybrid approach - use local first, fallback to ChatGPT for complex content
        public async Task<string> GetHybridSummaryAsync(string userInput, string? filePath = null, bool useLocalFirst = true)
        {
            if (useLocalFirst)
            {
                try
                {
                    var localSummary = await GetSummaryAsync(userInput, filePath);

                    // Check if local summary seems adequate (basic heuristic)
                    if (localSummary.Length > 50 && !localSummary.StartsWith("Error"))
                    {
                        return localSummary + "\n\n[Generated locally]";
                    }
                }
                catch
                {
                    // Fall through to ChatGPT if local fails
                }
            }

            // Fallback to your existing ChatGPT method
            // return await GetChatGptResponseAsync(userInput, filePath);
            return "Would fallback to ChatGPT here";
        }
    }
}
