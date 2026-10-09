using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NavAid.Api.Models;
using Microsoft.Extensions.Options;

namespace NavAid.Api.Services;

public interface IAIExplanationService
{
    Task<ExplanationResult> ExplainAsync(
        ExplanationPayload payload,
        string? modelOverride = null,
        CancellationToken ct = default);
}

/// Generates the explanation prose.
///
/// NOTE THE CONSTRUCTOR. This class takes an HttpClient, its options and a
/// logger. It has no DbContext, no SpatialAnalysisService, no scoring service
/// and no connection string. The separation the project claims — that the
/// language model explains figures it is handed and cannot reach the data
/// itself — is enforced here by the dependency list, not by instruction.
/// Nothing in this file can produce a number.
///
/// Every path returns an ExplanationResult. A failed API call, a malformed
/// response or a disabled key all degrade to the deterministic writer rather
/// than surfacing an error, because an explanation the user can read is worth
/// more than a stack trace, and the deterministic one is equally correct.
public sealed class AIExplanationService : IAIExplanationService
{
    private readonly HttpClient _http;
    private readonly GeminiOptions _options;
    private readonly ILogger<AIExplanationService> _logger;

    public AIExplanationService(
        HttpClient http,
        IOptions<GeminiOptions> options,
        ILogger<AIExplanationService> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ExplanationResult> ExplainAsync(
        ExplanationPayload payload,
        string? modelOverride = null,
        CancellationToken ct = default)
    {
        var model = string.IsNullOrWhiteSpace(modelOverride) ? _options.Model : modelOverride;
        var hash = PayloadHash(payload, model);

        // -- Recorded response, if one exists --------------------------------
        if (_options.Mode is ServiceMode.Cached or ServiceMode.Offline)
        {
            var recorded = await TryReadRecordedAsync(hash, ct);
            if (recorded is not null)
            {
                _logger.LogInformation("Explanation for {Area} served from recorded response {Hash}",
                    payload.Subject.Name, hash);
                return Finish(payload, recorded, ExplanationSource.Model, null);
            }
        }

        if (_options.Mode == ServiceMode.Offline)
        {
            return TemplateExplanationWriter.Write(payload,
                "Running in offline mode with no recorded response for this area.");
        }

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _logger.LogWarning("No Gemini API key configured; falling back to the deterministic writer.");
            return TemplateExplanationWriter.Write(payload, "No API key is configured.");
        }

        // -- Live call, one retry -------------------------------------------
        try
        {
            var sections = await GenerateAsync(payload, model, ct);

            if (_options.RecordResponses)
                await TryWriteRecordedAsync(hash, sections, ct);

            var result = Finish(payload, sections, ExplanationSource.Model, null);

            _logger.LogInformation(
                "Explanation for {Area}: model={Model} outcome={Outcome} matched={Matched}/{Checked} findings={Findings}",
                payload.Subject.Name, model,
                result.Verification?.Outcome, result.Verification?.FiguresMatched,
                result.Verification?.FiguresChecked, result.Verification?.Findings.Count ?? 0);

            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Gemini call timed out after {Seconds}s for {Area}; falling back.",
                _options.TimeoutSeconds, payload.Subject.Name);

            return TemplateExplanationWriter.Write(payload,
                "The explanation service did not respond in time, so this was generated without AI. The figures are identical.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Gemini call failed for {Area}; falling back to the deterministic writer.",
                payload.Subject.Name);

            return TemplateExplanationWriter.Write(payload,
                "The explanation service was unavailable, so this was generated without AI. The figures are identical.");
        }
    }

    // =======================================================================

    private async Task<List<ExplanationSection>> GenerateAsync(
        ExplanationPayload payload,
        string model,
        CancellationToken ct)
    {
        var request = new
        {
            systemInstruction = new
            {
                parts = new[] { new { text = PromptBuilder.SystemInstruction(payload) } }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = PromptBuilder.UserMessage(payload) } }
                }
            },
            generationConfig = new
            {
                temperature = _options.Temperature,
                responseMimeType = "application/json",
                responseSchema = ResponseSchema(),
                // thinkingConfig = new { thinkingBudget = _options.ThinkingBudget }
            }
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

        var url = $"{_options.Endpoint}/models/{model}:generateContent";
        var json = JsonSerializer.Serialize(request);

        _logger.LogInformation("Gemini request: model={Model} temp={Temp} bodyLength={Len}",
            _options.Model, _options.Temperature, json.Length);

        //_logger.LogInformation("generationConfig: {Config}",
        //    JsonSerializer.Serialize(new
        //    {
        //        temperature = _options.Temperature,
        //        responseMimeType = "application/json",
        //        responseSchema = ResponseSchema()
        //    }));

        _logger.LogInformation("systemInstruction head: {Head}",
            PromptBuilder.SystemInstruction(payload)[..Math.Min(200, PromptBuilder.SystemInstruction(payload).Length)]);

        using var message = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        message.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        message.Headers.Add("x-goog-api-key", _options.ApiKey);

        using var response = await _http.SendAsync(message, cts.Token);
        var body = await response.Content.ReadAsStringAsync(cts.Token);

        if (!response.IsSuccessStatusCode)
        {
            // 429 is not retried. Quota exhaustion does not resolve in a second,
            // and retrying makes it worse for whoever is next.
            throw new InvalidOperationException(
                $"Gemini returned {(int)response.StatusCode}: {Truncate(body, 400)}");
        }

        return ParseSections(body);
    }

    /// <summary>
    /// OpenAPI-subset schema. Structured output is the strongest decoding-level
    /// constraint available: it removes the model's freedom to return prose
    /// around the JSON, preamble, or a different shape under pressure.
    /// </summary>
    private static object ResponseSchema() => new
    {
        type = "object",
        properties = new
        {
            sections = new
            {
                type = "array",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        heading = new { type = "string" },
                        body = new { type = "string" },
                        citedFactIds = new
                        {
                            type = "array",
                            items = new { type = "string" }
                        }
                    },
                    required = new[] { "heading", "body", "citedFactIds" },
                    // propertyOrdering = new[] { "heading", "body", "citedFactIds" }
                }
            }
        },
        required = new[] { "sections" }
    };

    private static List<ExplanationSection> ParseSections(string body)
    {
        using var doc = JsonDocument.Parse(body);

        var text = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")
            .EnumerateArray()
            .Select(p => p.TryGetProperty("text", out var t) ? t.GetString() : null)
            .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));

        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException("Gemini returned an empty response.");

        using var inner = JsonDocument.Parse(text);

        var sections = inner.RootElement.GetProperty("sections").EnumerateArray()
            .Select(s => new ExplanationSection
            {
                Heading = s.GetProperty("heading").GetString() ?? "Explanation",
                Body = s.GetProperty("body").GetString() ?? string.Empty,
                CitedFactIds = s.TryGetProperty("citedFactIds", out var ids)
                    ? ids.EnumerateArray().Select(i => i.GetString() ?? string.Empty).ToList()
                    : new List<string>(),
                IsVerbatim = false
            })
            .Where(s => !string.IsNullOrWhiteSpace(s.Body))
            .ToList();

        if (sections.Count == 0)
            throw new InvalidOperationException("Gemini returned no usable sections.");

        return sections;
    }

    // =======================================================================

    /// <summary>
    /// Appends the builder-supplied sections, verifies the generated prose
    /// against the FULL ledger, and assembles the result.
    /// </summary>
    private static ExplanationResult Finish(
        ExplanationPayload payload,
        List<ExplanationSection> generated,
        ExplanationSource source,
        string? fallbackReason)
    {
        var sections = new List<ExplanationSection>(generated);

        if (!payload.Subject.IsScored)
        {
            sections.Add(new ExplanationSection
            {
                Heading = "Not included in the ranking",
                Body = payload.Subject.ExclusionReason ?? "This area is excluded from the ranking.",
                CitedFactIds = Array.Empty<string>(),
                IsVerbatim = true
            });
        }

        sections.Add(new ExplanationSection
        {
            Heading = "Worth bearing in mind",
            Body = string.Join(" ", payload.Caveats),
            CitedFactIds = Array.Empty<string>(),
            IsVerbatim = true
        });

        var verifiable = string.Join("\n\n",
            sections.Where(s => !s.IsVerbatim).Select(s => s.Body));

        var citedIds = sections
            .Where(s => !s.IsVerbatim)
            .SelectMany(s => s.CitedFactIds)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return new ExplanationResult
        {
            Source = source,
            Sections = sections,
            Payload = payload,
            Verification = ExplanationVerifier.Verify(payload, verifiable, citedIds),
            FallbackReason = fallbackReason
        };
    }

    // -- Recorded responses --------------------------------------------------

    /// <summary>
    /// Hash of the prompt, not of the area. Two requests for the same area
    /// under different weights are different explanations and must not share
    /// a cache entry.
    /// </summary>
    private string PayloadHash(ExplanationPayload payload, string model)
    {
        var material = model
            + _options.Temperature.ToString("0.00")
            + PromptBuilder.SystemInstruction(payload)
            + PromptBuilder.UserMessage(payload);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    private async Task<List<ExplanationSection>?> TryReadRecordedAsync(string hash, CancellationToken ct)
    {
        try
        {
            var path = Path.Combine(_options.RecordedResponsesPath, $"{hash}.json");
            if (!File.Exists(path)) return null;

            var json = await File.ReadAllTextAsync(path, ct);
            return JsonSerializer.Deserialize<List<ExplanationSection>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read recorded explanation {Hash}", hash);
            return null;
        }
    }

    private async Task TryWriteRecordedAsync(string hash, List<ExplanationSection> sections, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(_options.RecordedResponsesPath);
            var path = Path.Combine(_options.RecordedResponsesPath, $"{hash}.json");
            await File.WriteAllTextAsync(path,
                JsonSerializer.Serialize(sections, new JsonSerializerOptions { WriteIndented = true }), ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not record explanation {Hash}", hash);
        }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "...";
}