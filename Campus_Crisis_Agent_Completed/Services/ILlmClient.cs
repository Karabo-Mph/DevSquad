namespace CampusCrisisAgent.Services;

/// <summary>
/// Do not implement the HTTP client in this solution.
/// When you add GroqLlmClient, register it in Program.cs:
///   builder.Services.AddSingleton&lt;ILlmClient, GroqLlmClient&gt;();
/// AssessmentService will then call AssessAsync; until then it stays fully rule-based.
/// </summary>
public interface ILlmClient
{
    /// <summary>Return a model completion for the given prompt (typically JSON).</summary>
    Task<string> AssessAsync(string prompt);
}
