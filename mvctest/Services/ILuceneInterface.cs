using mvctest.Models;

namespace mvctest.Services
{
    public interface ILuceneInterface
    {
        void ClearIndex(string confirmation);
        void InitializeLucene();
        List<SearchResultModel> SearchFiles(string query);
        List<SearchResultModel> SearchFilesInPaths(string query, List<string> filePaths);
        List<SearchResultModel> SemanticSearch(string query, List<string> filePaths = null, int maxResults = 10);
        void IndexFilesInternal(List<string> filesToIndex, bool forceReindex);
        List<string> GetAllContentSnippets(string content, string query, int maxLength);
        void ShowIndexStats();
        void CleanupLucene();
        void BatchIndexFilesFromContentManager(List<string> directories);
        void CommitIndex();
        Task<bool> ProcessFilesInDirectory(string directoryPath);

    }
}
