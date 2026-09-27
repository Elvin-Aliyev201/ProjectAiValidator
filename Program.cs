

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using LookFutureApi.Services;
using Polly;
using Polly.Extensions.Http;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ─────────── JSON Serialization ───────────
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

// ─────────── CORS ───────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy
            .AllowAnyOrigin()   // Dev convenience - restrict in production
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

// ─────────── Retry Policy for OpenRouter (429 / 503) ───────────
var retryPolicy = HttpPolicyExtensions
    .HandleTransientHttpError()
    .OrResult(msg => msg.StatusCode == HttpStatusCode.TooManyRequests)
    .WaitAndRetryAsync(
        retryCount: 2,
        sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(1500 * attempt),
        onRetry: (outcome, delay, retryAttempt, _) =>
        {
            Console.WriteLine(
                "[Polly] OpenRouter retry #{0} after {1:F0}ms — Status: {2}",
                retryAttempt, delay.TotalMilliseconds,
                outcome.Result?.StatusCode.ToString() ?? outcome.Exception?.Message ?? "unknown");
        });

// ─────────── HttpClient Factories ───────────
builder.Services.AddHttpClient("OpenRouter", client =>
{
    client.Timeout = TimeSpan.FromSeconds(120);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
})
.AddPolicyHandler(retryPolicy);

builder.Services.AddHttpClient("Supabase", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

// ─────────── Services ───────────
builder.Services.AddScoped<IAnalysisService, OpenRouterAnalysisService>();
builder.Services.AddScoped<ISupabaseService, SupabaseService>();

// ─────────── Authentication ───────────
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => {
        options.TokenValidationParameters = new TokenValidationParameters {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]))
        };
    });
builder.Services.AddAuthorization();

// ─────────── Swagger / OpenAPI ───────────
builder.Services.AddOpenApi();

var app = builder.Build();

// ─────────── Middleware Pipeline ───────────
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("AllowFrontend");

// Serve the frontend from wwwroot
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
