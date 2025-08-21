using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace mvctest.Services
{
    public interface ISessionSearchManager
    {
        Task<string> CreateSearchSessionAsync(List<Dictionary<string, string>> searchFilters, HttpContext httpContext);
        Task<List<Dictionary<string, string>>?> GetSearchFiltersAsync(string searchId, HttpContext httpContext);
        Task<bool> IsSearchSessionValidAsync(string searchId, HttpContext httpContext);
        Task CleanupExpiredSearchesAsync(HttpContext httpContext);
        Task<int> GetActiveSearchCountAsync(HttpContext httpContext);
    }

    public class SessionSearchManager : ISessionSearchManager
    {
        private readonly ILogger<SessionSearchManager> _logger;
        private const string TIMESTAMP_PREFIX = "SearchTime_";
        private const string FILTER_PREFIX = "SearchFilters_";
        private const string CURRENT_SEARCH_KEY = "CurrentSearchId";
        private const string SESSION_KEYS_KEY = "_SessionKeys";
        private const int MAX_SEARCH_AGE_MINUTES = 30;
        private const int MAX_SEARCHES_PER_SESSION = 5; // Limit to prevent memory issues

        public SessionSearchManager(ILogger<SessionSearchManager> logger)
        {
            _logger = logger;
        }

        public async Task<string> CreateSearchSessionAsync(List<Dictionary<string, string>> searchFilters, HttpContext httpContext)
        {
            try
            {
                // Clean up old searches first
                await CleanupExpiredSearchesAsync(httpContext);

                // Check if we have too many searches for this session
                var activeCount = await GetActiveSearchCountAsync(httpContext);
                if (activeCount >= MAX_SEARCHES_PER_SESSION)
                {
                    _logger.LogWarning("⚠️ Session has too many active searches ({ActiveCount}), forcing cleanup", activeCount);
                    await ForceCleanupOldestSearchAsync(httpContext);
                }

                // Generate unique search ID
                var searchId = Guid.NewGuid().ToString("N")[..12]; // Shorter ID for readability
                var timestamp = DateTime.UtcNow;

                // Store search filters and timestamp
                var filterKey = $"{FILTER_PREFIX}{searchId}";
                var timestampKey = $"{TIMESTAMP_PREFIX}{searchId}";
                
                httpContext.Session.SetString(filterKey, JsonSerializer.Serialize(searchFilters));
                httpContext.Session.SetString(timestampKey, timestamp.ToString("O")); // ISO 8601 format
                
                // Track these keys for cleanup
                await TrackSessionKeyAsync(filterKey, httpContext);
                await TrackSessionKeyAsync(timestampKey, httpContext);

                // Store current search ID
                httpContext.Session.SetString(CURRENT_SEARCH_KEY, searchId);
                
                _logger.LogDebug("🔍 Created search session: {SearchId} with {FilterCount} filters", 
                    searchId, searchFilters.Count);

                return searchId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to create search session");
                throw;
            }
        }

        public async Task<List<Dictionary<string, string>>?> GetSearchFiltersAsync(string searchId, HttpContext httpContext)
        {
            try
            {
                var filterKey = $"{FILTER_PREFIX}{searchId}";
                var filtersJson = httpContext.Session.GetString(filterKey);
                
                if (string.IsNullOrEmpty(filtersJson))
                {
                    _logger.LogDebug("🔍 Search filters not found for ID: {SearchId}", searchId);
                    return null;
                }

                // Check if search is still valid (not expired)
                if (!await IsSearchSessionValidAsync(searchId, httpContext))
                {
                    _logger.LogDebug("⏰ Search session expired: {SearchId}", searchId);
                    await RemoveSearchSessionAsync(searchId, httpContext);
                    return null;
                }

                var filters = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(filtersJson);
                
                _logger.LogDebug("✅ Retrieved {FilterCount} filters for search: {SearchId}", 
                    filters?.Count ?? 0, searchId);

                return filters;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to get search filters for ID: {SearchId}", searchId);
                return null;
            }
        }

        public async Task<bool> IsSearchSessionValidAsync(string searchId, HttpContext httpContext)
        {
            try
            {
                var timestampKey = $"{TIMESTAMP_PREFIX}{searchId}";
                var timestampStr = httpContext.Session.GetString(timestampKey);
                
                if (string.IsNullOrEmpty(timestampStr))
                {
                    return false;
                }

                if (!DateTime.TryParse(timestampStr, out var timestamp))
                {
                    return false;
                }

                var age = DateTime.UtcNow.Subtract(timestamp);
                var isValid = age.TotalMinutes <= MAX_SEARCH_AGE_MINUTES;
                
                if (!isValid)
                {
                    _logger.LogDebug("⏰ Search session expired: {SearchId} (Age: {Age:F1} minutes)", 
                        searchId, age.TotalMinutes);
                }

                return isValid;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to validate search session: {SearchId}", searchId);
                return false;
            }
        }

        public async Task CleanupExpiredSearchesAsync(HttpContext httpContext)
        {
            try
            {
                var currentTime = DateTime.UtcNow;
                var keysToRemove = new List<string>();
                var searchTimestamps = await GetAllSearchTimestampsAsync(httpContext);
                
                foreach (var kvp in searchTimestamps)
                {
                    var searchId = kvp.Key;
                    var timestamp = kvp.Value;
                    
                    // Check if search is older than MAX_SEARCH_AGE_MINUTES
                    if (currentTime.Subtract(timestamp).TotalMinutes > MAX_SEARCH_AGE_MINUTES)
                    {
                        keysToRemove.Add($"{FILTER_PREFIX}{searchId}");
                        keysToRemove.Add($"{TIMESTAMP_PREFIX}{searchId}");
                        
                        _logger.LogDebug("🧹 Cleaning up expired search: {SearchId} (Age: {Age:F1} minutes)", 
                            searchId, currentTime.Subtract(timestamp).TotalMinutes);
                    }
                }
                
                // Remove old search data from session
                foreach (var key in keysToRemove)
                {
                    httpContext.Session.Remove(key);
                    await RemoveTrackedKeyAsync(key, httpContext);
                }
                
                if (keysToRemove.Count > 0)
                {
                    _logger.LogInformation("✅ Cleaned up {SearchCount} expired searches", keysToRemove.Count / 2);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to cleanup expired searches");
            }
        }

        public async Task<int> GetActiveSearchCountAsync(HttpContext httpContext)
        {
            try
            {
                var searchTimestamps = await GetAllSearchTimestampsAsync(httpContext);
                var currentTime = DateTime.UtcNow;
                
                var activeCount = searchTimestamps.Values.Count(timestamp => 
                    currentTime.Subtract(timestamp).TotalMinutes <= MAX_SEARCH_AGE_MINUTES);
                
                return activeCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to get active search count");
                return 0;
            }
        }

        private async Task<Dictionary<string, DateTime>> GetAllSearchTimestampsAsync(HttpContext httpContext)
        {
            var timestamps = new Dictionary<string, DateTime>();
            
            try
            {
                var sessionKeys = await GetSessionKeysAsync(httpContext);
                
                foreach (var key in sessionKeys)
                {
                    if (key.StartsWith(TIMESTAMP_PREFIX))
                    {
                        var searchId = key.Substring(TIMESTAMP_PREFIX.Length);
                        var timestampStr = httpContext.Session.GetString(key);
                        
                        if (!string.IsNullOrEmpty(timestampStr) && DateTime.TryParse(timestampStr, out var timestamp))
                        {
                            timestamps[searchId] = timestamp;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to get search timestamps");
            }
            
            return timestamps;
        }

        private async Task<List<string>> GetSessionKeysAsync(HttpContext httpContext)
        {
            var keys = new List<string>();
            
            try
            {
                var sessionKeysJson = httpContext.Session.GetString(SESSION_KEYS_KEY);
                
                if (!string.IsNullOrEmpty(sessionKeysJson))
                {
                    keys = JsonSerializer.Deserialize<List<string>>(sessionKeysJson) ?? new List<string>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to get session keys");
            }
            
            return keys;
        }

        private async Task TrackSessionKeyAsync(string key, HttpContext httpContext)
        {
            try
            {
                var keys = await GetSessionKeysAsync(httpContext);
                
                if (!keys.Contains(key))
                {
                    keys.Add(key);
                    httpContext.Session.SetString(SESSION_KEYS_KEY, JsonSerializer.Serialize(keys));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to track session key: {Key}", key);
            }
        }

        private async Task RemoveTrackedKeyAsync(string key, HttpContext httpContext)
        {
            try
            {
                var keys = await GetSessionKeysAsync(httpContext);
                
                if (keys.Remove(key))
                {
                    httpContext.Session.SetString(SESSION_KEYS_KEY, JsonSerializer.Serialize(keys));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to remove tracked key: {Key}", key);
            }
        }

        private async Task ForceCleanupOldestSearchAsync(HttpContext httpContext)
        {
            try
            {
                var searchTimestamps = await GetAllSearchTimestampsAsync(httpContext);
                
                if (searchTimestamps.Any())
                {
                    // Remove the oldest search
                    var oldestSearch = searchTimestamps.OrderBy(kvp => kvp.Value).First();
                    var searchId = oldestSearch.Key;
                    
                    await RemoveSearchSessionAsync(searchId, httpContext);
                    
                    _logger.LogInformation("🧹 Force-cleaned oldest search session: {SearchId}", searchId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to force cleanup oldest search");
            }
        }

        private async Task RemoveSearchSessionAsync(string searchId, HttpContext httpContext)
        {
            try
            {
                var filterKey = $"{FILTER_PREFIX}{searchId}";
                var timestampKey = $"{TIMESTAMP_PREFIX}{searchId}";
                
                httpContext.Session.Remove(filterKey);
                httpContext.Session.Remove(timestampKey);
                
                await RemoveTrackedKeyAsync(filterKey, httpContext);
                await RemoveTrackedKeyAsync(timestampKey, httpContext);
                
                // Clear current search ID if it matches
                var currentSearchId = httpContext.Session.GetString(CURRENT_SEARCH_KEY);
                if (currentSearchId == searchId)
                {
                    httpContext.Session.Remove(CURRENT_SEARCH_KEY);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Failed to remove search session: {SearchId}", searchId);
            }
        }
    }
}