using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace CampusCrisisAgent.Services;

/// <summary>
/// Implementation of ILlmClient using Groq API for enhanced incident assessment.
/// Requires GROQ_API_KEY to be configured in appsettings.json or environment variables.
/// </summary>
public class GroqLlmClient : ILlmClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly string _apiKey;
    private readonly string _model;
    private const string DefaultModel = "llama3-70b-8192";
    private const string ApiUrl = "https://api.groq.com/openai/v1/chat/completions";

    public GroqLlmClient(IConfiguration configuration, ILogger<GroqLlmClient> logger)
    {
        _logger = logger;
        _apiKey = configuration["GROQ_API_KEY"] ?? Environment.GetEnvironmentVariable("GROQ_API_KEY") ?? "";
        _model = configuration["GROQ_MODEL"] ?? DefaultModel;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("GROQ_API_KEY not configured. LLM features will be disabled.");
        }
        else
        {
            _logger.LogInformation("GroqLlmClient initialized with model: {Model}", _model);
        }
    }

    public async Task<string> AssessAsync(string prompt)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new InvalidOperationException("GROQ_API_KEY is not configured");
        }

        try
        {
            var requestBody = new
            {
                model = _model,
                messages = new[]
                {
                    new { role = "system", content = "You are a campus incident assessor. Return JSON only." },
                    new { role = "user", content = prompt }
                },
                temperature = 0.3,
                max_tokens = 200,
                response_format = new { type = "json_object" }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");

            _logger.LogDebug("Sending request to Groq API");
            var response = await _httpClient.PostAsync(ApiUrl, content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError("Groq API error: {StatusCode} - {Error}", response.StatusCode, error);
                throw new HttpRequestException($"Groq API returned {response.StatusCode}: {error}");
            }

            var responseJson = await response.Content.ReadAsStringAsync();
            _logger.LogDebug("Received response from Groq API");

            using var doc = JsonDocument.Parse(responseJson);
            var contentText = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            return contentText ?? "";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Groq API");
            throw;
        }
    }
}
