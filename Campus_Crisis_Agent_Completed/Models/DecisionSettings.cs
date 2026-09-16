namespace CampusCrisisAgent.Models;

/// <summary>
/// Configuration settings for decision-making logic.
/// </summary>
public class DecisionSettings
{
    public const string SectionName = "Decision";

    /// <summary>
    /// Minimum confidence threshold below which human review is required (default: 0.45)
    /// </summary>
    public double HumanReviewConfidenceFloor { get; set; } = 0.45;
}
