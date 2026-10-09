namespace GeoDSS.Api.Services;

public enum ServiceMode
{
    /// <summary>Call the API. Cache successful responses.</summary>
    Live,

    /// <summary>Serve from cache; call the API only on a miss.</summary>
    Cached,

    /// <summary>Never call the API. Serve from the recorded responses, then fall back to the template.</summary>
    Offline
}

public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Model identifier. Kept in configuration rather than hardcoded because
    /// Google retires and renames these on its own schedule; a wrong name
    /// returns a 404 that reads like an endpoint problem.
    /// </summary>
    public string Model { get; set; } = "gemini-3.6-flash";

    public string Endpoint { get; set; } = "https://generativelanguage.googleapis.com/v1beta";

    public ServiceMode Mode { get; set; } = ServiceMode.Cached;

    /// <summary>Low, not zero: zero can produce degenerate repetition on structured output.</summary>
    public double Temperature { get; set; } = 0.2;
    public int TimeoutSeconds { get; set; } = 90;

    /// <summary>Where recorded responses live, for Offline mode and demo safety.</summary>
    public string RecordedResponsesPath { get; set; } = "RecordedExplanations";

    /// <summary>Write successful live responses to RecordedResponsesPath.</summary>
    public bool RecordResponses { get; set; } = true;
}