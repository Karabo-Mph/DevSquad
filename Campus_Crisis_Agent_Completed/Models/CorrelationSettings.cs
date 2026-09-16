namespace CampusCrisisAgent.Models;

/// <summary>
/// Configuration settings for correlation scoring and thresholds.
/// </summary>
public class CorrelationSettings
{
    public const string SectionName = "Correlation";

    /// <summary>
    /// Weight for location similarity in correlation score (default: 0.35)
    /// </summary>
    public double WeightLocation { get; set; } = 0.35;

    /// <summary>
    /// Weight for type matching in correlation score (default: 0.25)
    /// </summary>
    public double WeightType { get; set; } = 0.25;

    /// <summary>
    /// Weight for time proximity in correlation score (default: 0.15)
    /// </summary>
    public double WeightTime { get; set; } = 0.15;

    /// <summary>
    /// Weight for text similarity in correlation score (default: 0.25)
    /// </summary>
    public double WeightText { get; set; } = 0.25;

    /// <summary>
    /// Minimum correlation score to consider reports related (default: 0.55)
    /// </summary>
    public double MatchThreshold { get; set; } = 0.55;

    /// <summary>
    /// Minimum correlation score to consider reports duplicates (default: 0.90)
    /// </summary>
    public double DuplicateThreshold { get; set; } = 0.90;

    /// <summary>
    /// Time window in hours for full time proximity score (default: 6)
    /// </summary>
    public double TimeFullScoreHours { get; set; } = 6;

    /// <summary>
    /// Time window in hours for zero time proximity score (default: 48)
    /// </summary>
    public double TimeZeroScoreHours { get; set; } = 48;
}
