using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LookFutureApi.Models;

namespace LookFutureApi.Services;

public interface ISupabaseService
{
    Task<IdeaRow?> SaveAnalysisAsync(AnalyzeIdeaRequestDto request, IdeaAnalysisResult result, string? userId = null);
    Task<List<IdeaRow>> GetHistoryAsync(string? userId = null, int limit = 20);
    Task<User?> GetUserByUsernameAsync(string username);
    Task<User?> CreateUserAsync(string username, string passwordHash);
}

public class SupabaseService : ISupabaseService
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly ILogger<SupabaseService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public SupabaseService(
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<SupabaseService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("Supabase");
        _baseUrl = config["Supabase:Url"]?.TrimEnd('/') ?? throw new ArgumentNullException("Supabase:Url");
        _apiKey = config["Supabase:ApiKey"] ?? throw new ArgumentNullException("Supabase:ApiKey");
        _logger = logger;
    }

    public async Task<IdeaRow?> SaveAnalysisAsync(AnalyzeIdeaRequestDto request, IdeaAnalysisResult result, string? userId = null)
    {
        // Clamp viability_score to valid range before persisting
        var clampedScore = Math.Clamp(result.ViabilityScore, 0, 100);

        // Serialize analysis_json cleanly — serialize to string first,
        // then include the raw string in the row so it isn't double-escaped
        string analysisJsonStr;
        try
        {
            analysisJsonStr = JsonSerializer.Serialize(result, JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to serialize analysis result for Supabase. Using empty JSON.");
            analysisJsonStr = "{}";
        }

        var row = new
        {
            title = result.Title ?? "Untitled",
            raw_description = request.RawDescription,
            business_type = result.BusinessType ?? "Unknown",
            viability_score = clampedScore,
            tam = result.Tam?.Value ?? "—",
            sam = result.Sam?.Value ?? "—",
            som = result.Som?.Value ?? "—",
            analysis_json = analysisJsonStr,
            user_id = userId
        };

        var jsonBody = JsonSerializer.Serialize(row, JsonOpts);

        try
        {
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/rest/v1/ideas")
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
            };
            httpRequest.Headers.Add("apikey", _apiKey);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            httpRequest.Headers.Add("Prefer", "return=representation");

            var response = await _httpClient.SendAsync(httpRequest);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Supabase insert failed ({Status}): {Body}. Continuing without persistence.",
                    response.StatusCode, responseBody);
                return null;
            }

            var rows = JsonSerializer.Deserialize<List<IdeaRow>>(responseBody, JsonOpts);
            return rows?.FirstOrDefault();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Supabase is unreachable. Analysis result will still be returned to client.");
            return null;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(ex, "Supabase request timed out. Analysis result will still be returned to client.");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Supabase save failed. Analysis result will still be returned to client.");
            return null;
        }
    }

    public async Task<List<IdeaRow>> GetHistoryAsync(string? userId = null, int limit = 20)
    {
        try
        {
            var url = $"{_baseUrl}/rest/v1/ideas?select=*&order=created_at.desc&limit={limit}";
            if (!string.IsNullOrEmpty(userId))
            {
                url += $"&user_id=eq.{userId}";
            }
            var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
            httpRequest.Headers.Add("apikey", _apiKey);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            var response = await _httpClient.SendAsync(httpRequest);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Supabase history fetch failed ({Status}): {Body}", response.StatusCode, responseBody);
                return new List<IdeaRow>();
            }

            return JsonSerializer.Deserialize<List<IdeaRow>>(responseBody, JsonOpts) ?? new List<IdeaRow>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch history from Supabase.");
            return new List<IdeaRow>();
        }
    }


    public async Task<User?> GetUserByUsernameAsync(string username)
    {
        try
        {
            var url = $"{_baseUrl}/rest/v1/users?username=eq.{Uri.EscapeDataString(username)}&select=*";
            var httpRequest = new HttpRequestMessage(HttpMethod.Get, url);
            httpRequest.Headers.Add("apikey", _apiKey);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            var response = await _httpClient.SendAsync(httpRequest);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Supabase get user by username failed: {Body}", responseBody);
                return null;
            }

            var users = JsonSerializer.Deserialize<List<User>>(responseBody, JsonOpts);
            return users?.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch user by username.");
            return null;
        }
    }

    public async Task<User?> CreateUserAsync(string username, string passwordHash)
    {
        var row = new
        {
            username = username,
            password_hash = passwordHash
        };

        var jsonBody = JsonSerializer.Serialize(row, JsonOpts);

        try
        {
            var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/rest/v1/users")
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
            };
            httpRequest.Headers.Add("apikey", _apiKey);
            httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            httpRequest.Headers.Add("Prefer", "return=representation");

            var response = await _httpClient.SendAsync(httpRequest);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Supabase create user failed ({Status}): {Body}", response.StatusCode, responseBody);
                return null;
            }

            var users = JsonSerializer.Deserialize<List<User>>(responseBody, JsonOpts);
            return users?.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Supabase create user failed.");
            return null;
        }
    }
}
