using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LookFutureApi.Models;

namespace LookFutureApi.Services;

public interface IAnalysisService
{
    Task<IdeaAnalysisResult> AnalyzeAsync(AnalyzeIdeaRequestDto request);
}

public class OpenRouterAnalysisService : IAnalysisService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<OpenRouterAnalysisService> _logger;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        WriteIndented = false
    };

    public OpenRouterAnalysisService(
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<OpenRouterAnalysisService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("OpenRouter");
        _config = config;
        _logger = logger;
    }

    public async Task<IdeaAnalysisResult> AnalyzeAsync(AnalyzeIdeaRequestDto request)
    {
        var apiKey = _config["OpenRouter:ApiKey"]!;
        var model = _config["OpenRouter:Model"]!;
        var baseUrl = _config["OpenRouter:BaseUrl"]!;
        var siteUrl = _config["OpenRouter:SiteUrl"] ?? "http://localhost:5000";
        var siteName = _config["OpenRouter:SiteName"] ?? "LookFuture AI";

        var systemPrompt = BuildSystemPrompt();
        var userPrompt = BuildUserPrompt(request);

        var payload = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = 0.3,
            top_p = 0.95,
            max_tokens = 4096,
            response_format = new { type = "json_object" }
        };

        var jsonPayload = JsonSerializer.Serialize(payload, JsonOpts);
        _logger.LogInformation("Sending analysis request to OpenRouter for: {Desc}",
            request.RawDescription[..Math.Min(80, request.RawDescription.Length)]);

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, baseUrl)
        {
            Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        httpRequest.Headers.Add("HTTP-Referer", siteUrl);
        httpRequest.Headers.Add("X-Title", siteName);

        HttpResponseMessage response;
        string responseBody;

        try
        {
            response = await _httpClient.SendAsync(httpRequest);
            responseBody = await response.Content.ReadAsStringAsync();
        }
        catch (TaskCanceledException)
        {
            _logger.LogError("OpenRouter request timed out after exhausting retries.");
            throw new InvalidOperationException("AI engine request timed out after retries. Please try again.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error contacting OpenRouter after retries.");
            throw;
        }

        // ── Handle rate-limit / unavailable after retries ──
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            var retryAfter = response.Headers.RetryAfter?.Delta?.TotalSeconds ?? 30;
            _logger.LogWarning("OpenRouter rate-limited after retries. Retry-After: {Seconds}s", retryAfter);
            throw new InvalidOperationException(
                $"AI engine is rate-limited. Please retry in {retryAfter:F0} seconds.");
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("OpenRouter returned {Status}: {Body}", response.StatusCode, responseBody);
            throw new InvalidOperationException($"OpenRouter API error ({response.StatusCode}): {responseBody}");
        }

        // Extract content from OpenRouter response envelope
        string rawContent;
        try
        {
            rawContent = ExtractContentFromResponse(responseBody);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract content from OpenRouter envelope. Body: {Body}",
                responseBody[..Math.Min(500, responseBody.Length)]);
            _logger.LogWarning("Returning structured fallback due to unparseable response.");
            return BuildFallbackResult(request);
        }

        _logger.LogInformation("Raw AI content length: {Len}", rawContent.Length);

        // Sanitize: extract clean JSON before deserializing
        var cleanJson = ExtractCleanJson(rawContent);

        try
        {
            var result = JsonSerializer.Deserialize<IdeaAnalysisResult>(cleanJson, JsonOpts);
            if (result == null)
            {
                _logger.LogWarning("Deserialization returned null. Returning fallback.");
                return BuildFallbackResult(request);
            }

            // Clamp values to valid ranges
            result.ViabilityScore = Math.Clamp(result.ViabilityScore, 0, 100);
            result.DemandMomentum = Math.Clamp(result.DemandMomentum, 0, 100);
            result.NeuralConfidence = Math.Clamp(result.NeuralConfidence, 0.0, 100.0);

            // Ensure search_trend has exactly 12 data points
            if (result.SearchTrend.DataPoints.Count != 12)
            {
                _logger.LogWarning("SearchTrend had {Count} points, padding/truncating to 12.",
                    result.SearchTrend.DataPoints.Count);
                result.SearchTrend = NormalizeSearchTrend(result.SearchTrend);
            }

            // Ensure required collections are non-null
            result.Competitors ??= new List<Competitor>();
            result.Risks ??= new List<RiskItem>();
            result.GtmVectors ??= new List<GtmVector>();

            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize AI response. Clean JSON: {Json}",
                cleanJson[..Math.Min(500, cleanJson.Length)]);
            _logger.LogWarning("Returning structured fallback due to JSON parse failure.");
            return BuildFallbackResult(request);
        }
    }

    /// <summary>
    /// Extracts the content string from the OpenRouter chat-completions envelope.
    /// </summary>
    private static string ExtractContentFromResponse(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var firstChoice = choices[0];
            if (firstChoice.TryGetProperty("message", out var message))
            {
                if (message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                {
                    var text = content.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }

                // If content was empty, fallback to reasoning property if present
                if (message.TryGetProperty("reasoning", out var reasoning) && reasoning.ValueKind == JsonValueKind.String)
                {
                    var text = reasoning.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
            }
        }

        // Fallback: maybe it's a direct response
        if (root.TryGetProperty("content", out var directContent) && directContent.ValueKind == JsonValueKind.String)
            return directContent.GetString() ?? string.Empty;

        throw new InvalidOperationException("Could not extract content from OpenRouter response.");
    }

    /// <summary>
    /// Bulletproof JSON extraction:
    /// 1. Strip &lt;think&gt;/&lt;reasoning&gt;/etc. blocks
    /// 2. Strip markdown code fences
    /// 3. Use bracket-depth matching to find the outer JSON object
    /// </summary>
    private static string ExtractCleanJson(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
            return "{}";

        // 1. Strip thinking/reasoning blocks (Nemotron, DeepSeek, etc.)
        string cleaned = Regex.Replace(rawResponse,
            @"<(think|reasoning|reflection|thought)>.*?</\1>",
            "", RegexOptions.Singleline).Trim();

        // 2. Strip markdown code fences
        var fenceMatch = Regex.Match(cleaned, @"```(?:json)?\s*([\s\S]*?)\s*```");
        if (fenceMatch.Success)
        {
            cleaned = fenceMatch.Groups[1].Value.Trim();
        }
        else
        {
            // Fallback: remove markdown fences at start/end
            if (cleaned.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned.Substring(7);
            else if (cleaned.StartsWith("```"))
                cleaned = cleaned.Substring(3);

            if (cleaned.EndsWith("```"))
                cleaned = cleaned.Substring(0, cleaned.Length - 3);

            cleaned = cleaned.Trim();
        }

        // 3. Bracket-depth matching: find the outer { ... } pair
        int startIdx = -1;
        int depth = 0;
        bool inString = false;
        bool escaped = false;

        for (int i = 0; i < cleaned.Length; i++)
        {
            char c = cleaned[i];

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\' && inString)
            {
                escaped = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString) continue;

            if (c == '{')
            {
                if (depth == 0) startIdx = i;
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0 && startIdx >= 0)
                {
                    return cleaned.Substring(startIdx, i - startIdx + 1);
                }
            }
        }

        // If bracket matching failed, try the naive approach
        int firstBrace = cleaned.IndexOf('{');
        int lastBrace = cleaned.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            return cleaned.Substring(firstBrace, lastBrace - firstBrace + 1);
        }

        return cleaned.Trim();
    }

    /// <summary>
    /// Returns a structured fallback when the AI produces unusable output,
    /// so the UI never crashes or hangs.
    /// </summary>
    private static IdeaAnalysisResult BuildFallbackResult(AnalyzeIdeaRequestDto request)
    {
        var shortDesc = request.RawDescription.Length > 40
            ? request.RawDescription[..40] + "..."
            : request.RawDescription;

        return new IdeaAnalysisResult
        {
            Title = "Təhlil tamamlanmadı (Yenidən cəhd edin)",
            BusinessType = "Müəyyən edilməyib",
            ViabilityScore = 50,
            Verdict = "RISKY",
            ExecutiveSummary = "Süni intellekt mühərrikindən cavab alınarkən gecikmə baş verdi. Zəhmət olmasa bir neçə saniyə sonra yenidən cəhd edin.",
            DemandMomentum = 50,
            RegulatoryRisk = "Tier-2",
            NeuralConfidence = 0.0,
            Tam = new MarketSize { Label = "TAM (Total)", Value = "—", Growth = "—", Description = "Data unavailable. Please retry.", Badge = "—" },
            Sam = new MarketSize { Label = "SAM (Serviceable)", Value = "—", Growth = "—", Description = "Data unavailable. Please retry.", Badge = "—" },
            Som = new MarketSize { Label = "SOM (Obtainable)", Value = "—", Growth = "—", Description = "Data unavailable. Please retry.", Badge = "—" },
            SearchTrend = new SearchTrend
            {
                DataPoints = new List<int> { 50, 50, 50, 50, 50, 50, 50, 50, 50, 50, 50, 50 },
                PeakMonth = "N/A",
                PeakChange = "N/A"
            },
            Competitors = new List<Competitor>
            {
                new() { Name = "Data Unavailable", Pricing = "—", WeaknessTag = "Retry", FeatureGap = "AI analysis incomplete. Please retry." }
            },
            Financials = new FinancialDefaults { DefaultArpu = 49, DefaultCac = 120, DefaultChurn = 4.5 },
            Risks = new List<RiskItem>
            {
                new() { Title = "Şəbəkə bağlantısı və ya API kvota gecikməsi", Impact = "Medium", Description = "The AI engine did not return valid data.", Mitigation = "Retry the analysis or adjust your input." },
                new() { Title = "Məlumat bazası ilə sinxronizasiya gözlənilir", Impact = "Medium", Description = "The AI engine did not return valid data.", Mitigation = "Retry the analysis or adjust your input." }
            },
            GtmVectors = new List<GtmVector>
            {
                new() { Title = "Yenidən yoxlama imkanı mövcuddur", YieldTag = "Pending", Description = "Submit your analysis again for complete results.", MetricLabel = "Status", MetricValue = "Incomplete" }
            }
        };
    }

    /// <summary>
    /// Normalizes search trend to exactly 12 data points.
    /// </summary>
    private static SearchTrend NormalizeSearchTrend(SearchTrend trend)
    {
        var points = trend.DataPoints ?? new List<int>();

        if (points.Count > 12)
            points = points.Take(12).ToList();

        while (points.Count < 12)
            points.Add(points.Count > 0 ? points.Last() : 50);

        // Clamp each value to 0–100
        for (int i = 0; i < points.Count; i++)
            points[i] = Math.Clamp(points[i], 0, 100);

        return new SearchTrend
        {
            DataPoints = points,
            PeakMonth = trend.PeakMonth ?? "N/A",
            PeakChange = trend.PeakChange ?? "N/A"
        };
    }

    private static string BuildSystemPrompt()
    {
        return """
        Sən peşəkar vençur kapitalı analitiki və bazar araşdırmaçısısan. İstifadəçi ideyanı hansı dildə daxil etsə belə, analiz nəticəsini yalnız və yalnız təmiz JSON formatında və tamamilə Azərbaycan dilində qaytarmalısan. 
        Sahələrin tərcümə qaydaları:
        - title: Layihənin qısa, cəlbedici adı (Azərbaycan dilində).
        - business_type: 'B2B SaaS', 'Lokal Fiziki Biznes', 'E-Ticarət' və s.
        - verdict: Bazarın potensialını, əsas üstünlüyünü və ilkin çətinliyini izah edən 2 cümləlik peşəkar rəy.
        - competitors: Hər bir rəqib üçün 'weakness' (zəif cəhəti) və 'gap' (bazardakı boşluq/fürsət) Azərbaycan dilində izah olunmalıdır.
        - swot: 'risks' (icra riskləri və maneələr) və 'gtm_vectors' (bazar strategiyası və əsas üstünlüklər) Azərbaycan dilində bəndlər şəklində yazılmalıdır.

        You are a strict JSON generator. Output ONLY a valid JSON object starting with { and ending with }. Do not include any explanations, reasoning, or markdown ticks.

        CRITICAL INSTRUCTION: Return ONLY a single raw JSON object. No markdown, no code fences, no conversational text, no explanation. Just the JSON object.

        The JSON must have this exact structure (use snake_case keys):
        {
          "title": "Short punchy name for the idea (max 6 words)",
          "business_type": "Category tag like B2B SaaS, D2C, Marketplace, etc.",
          "viability_score": 78,
          "verdict": "VIABLE or RISKY or NOT VIABLE",
          "executive_summary": "2-3 sentences. Include a key growth metric like +42% YoY. Mention both the opportunity and the primary risk.",
          "demand_momentum": 88,
          "regulatory_risk": "Tier-1 or Tier-2 or Tier-3",
          "neural_confidence": 91.4,
          "tam": {
            "label": "TAM (Total)",
            "value": "$4.2B",
            "growth": "+14.8% CAGR",
            "description": "Brief description of what the total market covers.",
            "badge": "+14.8% CAGR"
          },
          "sam": {
            "label": "SAM (Serviceable)",
            "value": "$240M",
            "growth": "$380 Avg ACV",
            "description": "Brief description of the serviceable segment.",
            "badge": "$380 Avg ACV"
          },
          "som": {
            "label": "SOM (Obtainable)",
            "value": "$1.8M",
            "growth": "410 Accounts",
            "description": "Brief Year 1-2 target description.",
            "badge": "410 Accounts"
          },
          "search_trend": {
            "data_points": [20, 25, 30, 35, 32, 40, 55, 62, 70, 95, 82, 78],
            "peak_month": "Oct",
            "peak_change": "+68%"
          },
          "competitors": [
            {
              "name": "Competitor Name",
              "pricing": "$37 - $97/mo",
              "weakness_tag": "Short weakness label",
              "feature_gap": "Describe the missing feature or weakness in 1-2 sentences."
            }
          ],
          "financials": {
            "default_arpu": 49,
            "default_cac": 120,
            "default_churn": 4.5
          },
          "risks": [
            {
              "title": "Risk title",
              "impact": "High or Medium or Low",
              "description": "1-2 sentences describing the risk.",
              "mitigation": "1-2 sentences describing the mitigation strategy."
            }
          ],
          "gtm_vectors": [
            {
              "title": "Go-to-market strategy name",
              "yield_tag": "High Yield or High Conversion or Defensibility Moat",
              "description": "1-2 sentences describing the strategy.",
              "metric_label": "Estimated CAC Impact or Benchmark Conversion or Viral Growth Coefficient",
              "metric_value": "-34% Acquisition Cost Reduction"
            }
          ]
        }

        Requirements:
        - viability_score must be an integer 0-100
        - demand_momentum must be an integer 0-100
        - neural_confidence must be a float 0-100
        - search_trend.data_points must be exactly 12 integers (Jan-Dec, 0-100 scale)
        - Include exactly 3 competitors with real or realistic company names
        - Include exactly 3 risks (one High, one Medium, one Low impact)
        - Include exactly 3 gtm_vectors
        - All monetary values in TAM/SAM/SOM should be formatted as $X.XB or $XXXM or $X.XM
        - financials.default_arpu should be realistic monthly price in USD
        - financials.default_cac should be realistic customer acquisition cost in USD
        - financials.default_churn should be realistic monthly churn percentage (1.5-10.0)
        - Retain Azerbaijani localization for all descriptive text (title, verdict, weaknesses, gaps, SWOT, summaries).
        """;
    }

    private static string BuildUserPrompt(AnalyzeIdeaRequestDto request)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Analyze this business idea: {request.RawDescription}");

        if (!string.IsNullOrWhiteSpace(request.Category))
            sb.AppendLine($"Category/Industry: {request.Category}");

        if (!string.IsNullOrWhiteSpace(request.Region))
            sb.AppendLine($"Target Region: {request.Region}");

        sb.AppendLine("Remember: Output ONLY a valid JSON object starting with { and ending with }. Do not include any explanations, reasoning, or markdown ticks.");
        return sb.ToString();
    }
}
