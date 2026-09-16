namespace CampusCrisisAgent.Models;

public class PlannedAction
{
    public string Type { get; set; } = "";
    public string Service { get; set; } = "";
}

public class ActionDecision
{
    public string Decision { get; set; } = "NO_ACTION";
    public List<PlannedAction> Actions { get; set; } = new();
    public string Service { get; set; } = "";
    public bool HumanReviewRequired { get; set; }
    public string NextStatus { get; set; } = IncidentStatuses.Open;
    public string Reason { get; set; } = "";
}

public class AssessmentResult
{
    public string Severity { get; set; } = "LOW";
    public double Confidence { get; set; } = 0.5;
    public bool ConflictingEvidence { get; set; }
    public string Rationale { get; set; } = "";
}

public class CorrelationResult
{
    public Incident? Match { get; set; }
    public double BestScore { get; set; }
    public string Relationship { get; set; } = "new";
    public bool LooksLikeDuplicate { get; set; }
    public string Notes { get; set; } = "";
}
