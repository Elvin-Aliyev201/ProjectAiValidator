namespace LookFutureApi.Models;

// ─────────── Request DTOs ───────────

public class AnalyzeIdeaRequestDto
{
    public string RawDescription { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? Region { get; set; }
}

// ─────────── AI Analysis Result Models ───────────

public class IdeaAnalysisResult
{
    public string Title { get; set; } = string.Empty;
    public string BusinessType { get; set; } = string.Empty;
    public int ViabilityScore { get; set; }
    public string Verdict { get; set; } = string.Empty;
    public string ExecutiveSummary { get; set; } = string.Empty;
    public int DemandMomentum { get; set; }
    public string RegulatoryRisk { get; set; } = "Tier-2";
    public double NeuralConfidence { get; set; }
    public MarketSize Tam { get; set; } = new();
    public MarketSize Sam { get; set; } = new();
    public MarketSize Som { get; set; } = new();
    public SearchTrend SearchTrend { get; set; } = new();
    public List<Competitor> Competitors { get; set; } = new();
    public FinancialDefaults Financials { get; set; } = new();
    public List<RiskItem> Risks { get; set; } = new();
    public List<GtmVector> GtmVectors { get; set; } = new();
}

public class MarketSize
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Growth { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Badge { get; set; } = string.Empty;
}

public class SearchTrend
{
    /// <summary>12 monthly data points (0–100 scale)</summary>
    public List<int> DataPoints { get; set; } = new();
    public string PeakMonth { get; set; } = string.Empty;
    public string PeakChange { get; set; } = string.Empty;
}

public class Competitor
{
    public string Name { get; set; } = string.Empty;
    public string Pricing { get; set; } = string.Empty;
    public string WeaknessTag { get; set; } = string.Empty;
    public string FeatureGap { get; set; } = string.Empty;
}

public class FinancialDefaults
{
    public int DefaultArpu { get; set; } = 49;
    public int DefaultCac { get; set; } = 120;
    public double DefaultChurn { get; set; } = 4.5;
}

public class RiskItem
{
    public string Title { get; set; } = string.Empty;
    public string Impact { get; set; } = "Medium";
    public string Description { get; set; } = string.Empty;
    public string Mitigation { get; set; } = string.Empty;
}

public class GtmVector
{
    public string Title { get; set; } = string.Empty;
    public string YieldTag { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string MetricLabel { get; set; } = string.Empty;
    public string MetricValue { get; set; } = string.Empty;
}

// ─────────── Supabase Row ───────────

public class IdeaRow
{
    public string? Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string RawDescription { get; set; } = string.Empty;
    public string BusinessType { get; set; } = string.Empty;
    public int ViabilityScore { get; set; }
    public string Tam { get; set; } = string.Empty;
    public string Sam { get; set; } = string.Empty;
    public string Som { get; set; } = string.Empty;
    public string AnalysisJson { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

// ─────────── API Response Wrapper ───────────

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Error { get; set; }

    public static ApiResponse<T> Ok(T data) => new() { Success = true, Data = data };
    public static ApiResponse<T> Fail(string error) => new() { Success = false, Error = error };
}
