using LookFutureApi.Models;
using LookFutureApi.Services;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace LookFutureApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class IdeasController : ControllerBase
{
    private readonly IAnalysisService _analysisService;
    private readonly ISupabaseService _supabaseService;
    private readonly ILogger<IdeasController> _logger;

    public IdeasController(
        IAnalysisService analysisService,
        ISupabaseService supabaseService,
        ILogger<IdeasController> logger)
    {
        _analysisService = analysisService;
        _supabaseService = supabaseService;
        _logger = logger;
    }

    /// <summary>
    /// Analyze a business idea using the AI engine and persist to Supabase.
    /// </summary>
    [HttpPost("analyze")]
    public async Task<ActionResult<ApiResponse<IdeaAnalysisResult>>> Analyze(
        [FromBody] AnalyzeIdeaRequestDto request)
    {
        if (string.IsNullOrWhiteSpace(request.RawDescription))
            return BadRequest(ApiResponse<IdeaAnalysisResult>.Fail("Business idea description is required."));

        if (request.RawDescription.Length < 10)
            return BadRequest(ApiResponse<IdeaAnalysisResult>.Fail("Please provide a more detailed description (at least 10 characters)."));

        try
        {
            _logger.LogInformation("Starting analysis for idea: {Desc}",
                request.RawDescription[..Math.Min(80, request.RawDescription.Length)]);

            // 1. Call OpenRouter AI
            var result = await _analysisService.AnalyzeAsync(request);

            // 2. Persist to Supabase — fire-and-forget but capture reference to
            //    the scoped service before the Task.Run so we don't lose the scope.
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var supabaseSvc = _supabaseService;
            var logger = _logger;
            _ = Task.Run(async () =>
            {
                try
                {
                    await supabaseSvc.SaveAnalysisAsync(request, result, userId);
                    logger.LogInformation("Analysis saved to Supabase for: {Title}", result.Title);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Background Supabase save failed.");
                }
            });

            return Ok(ApiResponse<IdeaAnalysisResult>.Ok(result));
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("rate-limited"))
        {
            _logger.LogWarning(ex, "Rate-limited by OpenRouter.");
            return StatusCode(429, ApiResponse<IdeaAnalysisResult>.Fail(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Analysis failed for idea.");
            return StatusCode(502, ApiResponse<IdeaAnalysisResult>.Fail($"AI engine error: {ex.Message}"));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error during analysis.");
            return StatusCode(503, ApiResponse<IdeaAnalysisResult>.Fail("Unable to reach the AI engine. Please try again."));
        }
        catch (TaskCanceledException)
        {
            return StatusCode(504, ApiResponse<IdeaAnalysisResult>.Fail("AI engine request timed out. Please try again."));
        }
    }

    /// <summary>
    /// Get recently analyzed ideas from history.
    /// </summary>
    [HttpGet("history")]
    public async Task<ActionResult<ApiResponse<List<IdeaRow>>>> GetHistory([FromQuery] int limit = 20)
    {
        try
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var history = await _supabaseService.GetHistoryAsync(userId, Math.Clamp(limit, 1, 100));
            return Ok(ApiResponse<List<IdeaRow>>.Ok(history));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve analysis history.");
            return StatusCode(500, ApiResponse<List<IdeaRow>>.Fail("Failed to retrieve history."));
        }
    }
}
