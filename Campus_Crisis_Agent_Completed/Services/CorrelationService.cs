using CampusCrisisAgent.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CampusCrisisAgent.Services;

/// <summary>
/// Pure clustering: no shared incident IDs are assumed to exist on incoming reports.
/// Uses configurable weights and thresholds from appsettings.json.
/// </summary>
public class CorrelationService
{
    private readonly ILogger _logger;
    private readonly CorrelationSettings _settings;

    public CorrelationService(ILogger<CorrelationService> logger, IOptions<CorrelationSettings> settings)
    {
        _logger = logger;
        _settings = settings.Value;
        _logger.LogInformation("CorrelationService initialized with thresholds: Match={MatchThreshold}, Duplicate={DuplicateThreshold}",
            _settings.MatchThreshold, _settings.DuplicateThreshold);
    }

    public CorrelationResult Correlate(CampusReport report, IncidentStateService state)
    {
        try
        {
            var open = state.GetOpenOrMonitoring();
            var bestOpen = BestMatch(report, open);
            if (bestOpen.Match != null && bestOpen.BestScore >= _settings.MatchThreshold)
                return bestOpen;

            var resolved = state.GetResolved();
            var bestResolved = BestMatch(report, resolved);
            if (bestResolved.Match != null && bestResolved.BestScore >= _settings.MatchThreshold)
            {
                bestResolved.Relationship = "update";
                bestResolved.Notes = TrimJoin(bestResolved.Notes, "matched a resolved incident — candidate for reopen");
                return bestResolved;
            }

            return new CorrelationResult
            {
                Match = null,
                BestScore = bestOpen.BestScore,
                Relationship = "new",
                Notes = bestOpen.BestScore > 0
                    ? $"no open match (best score {bestOpen.BestScore:0.00} < {_settings.MatchThreshold})"
                    : "no open incidents to compare"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Correlation error for report {ReportId}, treating as NEW", report.ReportId);
            return new CorrelationResult
            {
                Relationship = "new",
                Notes = $"correlation fallback (treated as NEW): {ex.Message}"
            };
        }
    }

    private CorrelationResult BestMatch(CampusReport report, IReadOnlyList<Incident> incidents)
    {
        Incident? best = null;
        var bestScore = 0.0;
        var duplicate = false;

        foreach (var incident in incidents)
        {
            var score = Score(report, incident);
            if (score > bestScore)
            {
                bestScore = score;
                best = incident;
                duplicate = score >= _settings.DuplicateThreshold;
            }
        }

        return new CorrelationResult
        {
            Match = best,
            BestScore = bestScore,
            Relationship = best != null && bestScore >= _settings.MatchThreshold ? "update" : "new",
            LooksLikeDuplicate = duplicate,
            Notes = duplicate ? "near-duplicate of an existing report cluster" : ""
        };
    }

    /// <summary>
    /// Calculates the correlation score between a report and an incident using multi-factor similarity.
    /// Formula: Score = (WeightLocation × LocationSimilarity) + (WeightType × TypeMatch) +
    ///           (WeightTime × TimeProximity) + (WeightText × TextSimilarity)
    /// </summary>
    /// <param name="report">The campus report to correlate.</param>
    /// <param name="incident">The existing incident to compare against.</param>
    /// <returns>A correlation score between 0.0 and 1.0, where higher values indicate stronger correlation.</returns>
    public double Score(CampusReport report, Incident incident)
    {
        var loc = LocationSimilarity(report.Location, incident.Location);
        var type = TypeMatch(report.Type, incident.Type);
        var time = TimeProximity(report.Timestamp, incident.LastUpdated, incident.OpenedAt);
        var text = TextSimilarity(report.Description, string.Join(" ", incident.Descriptions));
        return _settings.WeightLocation * loc + _settings.WeightType * type + _settings.WeightTime * time + _settings.WeightText * text;
    }

    private static double LocationSimilarity(string a, string b)
    {
        a = Normalize(a);
        b = Normalize(b);
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b) || a == "unknown" || b == "unknown")
            return 0.15; // missing location: weak prior, never throw
        if (a == b) return 1.0;
        if (a.Contains(b) || b.Contains(a)) return 0.85;
        var token = TokenOverlap(a, b);
        var edit = 1.0 - NormalizedLevenshtein(a, b);
        return Math.Max(token, edit);
    }

    private static double TypeMatch(string a, string b)
    {
        a = Normalize(a);
        b = Normalize(b);
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b) || a == "unclassified" || b == "unclassified")
            return 0.2;
        if (a == b) return 1.0;
        if (a.Contains(b) || b.Contains(a)) return 0.8;
        return 1.0 - NormalizedLevenshtein(a, b);
    }

    private double TimeProximity(DateTime reportTime, DateTime lastUpdated, DateTime openedAt)
    {
        if (reportTime == default || lastUpdated == default)
            return 0.4;

        var anchor = lastUpdated == default ? openedAt : lastUpdated;
        var hours = Math.Abs((reportTime - anchor).TotalHours);
        if (hours <= _settings.TimeFullScoreHours) return 1.0;
        if (hours >= _settings.TimeZeroScoreHours) return 0.0;
        return 1.0 - (hours - _settings.TimeFullScoreHours) / (_settings.TimeZeroScoreHours - _settings.TimeFullScoreHours);
    }

    private static double TextSimilarity(string a, string b)
    {
        a = Normalize(a);
        b = Normalize(b);
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return 0.1;
        if (a == b) return 1.0;
        var overlap = TokenOverlap(a, b);
        var edit = a.Length < 80 && b.Length < 80 ? 1.0 - NormalizedLevenshtein(a, b) : 0;
        return Math.Max(overlap, edit * 0.7);
    }

    private static double TokenOverlap(string a, string b)
    {
        var ta = Tokens(a);
        var tb = Tokens(b);
        if (ta.Count == 0 || tb.Count == 0) return 0;
        var inter = ta.Intersect(tb).Count();
        var union = ta.Union(tb).Count();
        return union == 0 ? 0 : (double)inter / union;
    }

    private static HashSet<string> Tokens(string text)
    {
        return text
            .Split(new[] { ' ', ',', '.', ';', ':', '-', '/', '\\', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 2)
            .ToHashSet();
    }

    private static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>
    /// Calculates the normalized Levenshtein distance between two strings.
    /// Normalization divides the edit distance by the maximum string length to get a value between 0.0 and 1.0.
    /// </summary>
    /// <param name="a">First string to compare.</param>
    /// <param name="b">Second string to compare.</param>
    /// <returns>Normalized edit distance where 0.0 = identical, 1.0 = completely different.</returns>
    private static double NormalizedLevenshtein(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0) return 0;
        var dist = Levenshtein(a, b);
        return (double)dist / Math.Max(a.Length, b.Length);
    }

    /// <summary>
    /// Calculates the Levenshtein edit distance between two strings using dynamic programming.
    /// Time Complexity: O(n × m) where n and m are string lengths.
    /// Space Complexity: O(n × m) for the DP matrix.
    /// </summary>
    /// <param name="a">First string to compare.</param>
    /// <param name="b">Second string to compare.</param>
    /// <returns>The minimum number of single-character edits (insertions, deletions, or substitutions) required to change one string into the other.</returns>
    private static int Levenshtein(string a, string b)
    {
        var n = a.Length;
        var m = b.Length;
        var d = new int[n + 1, m + 1];
        for (var i = 0; i <= n; i++) d[i, 0] = i;
        for (var j = 0; j <= m; j++) d[0, j] = j;
        for (var i = 1; i <= n; i++)
        {
            for (var j = 1; j <= m; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }
        return d[n, m];
    }

    private static string TrimJoin(string a, string b) =>
        string.IsNullOrWhiteSpace(a) ? b : $"{a}; {b}";
}
