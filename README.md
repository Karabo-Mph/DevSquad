Campus Crisis Agent
An intelligent incident management system for campus environments that processes reports, correlates incidents, assesses severity and coordinates emergency response services.
Architecture Overview
The system follows an agentic design pattern with a clear pipeline for processing incident reports:
```
Report → Observe → Correlate → Assess → Decide → Act → Monitor → Record
```
Core Components
1. ReportProcessor (Orchestrator)
Loads and processes campus reports from CSV files
Coordinates the entire processing pipeline
Maintains processing state and cursor position
Outputs predictions to `predictions.jsonl`

2. IncidentStateService (State Management)
In-memory store for all incident state
Thread-safe operations using ConcurrentDictionary
Manages incident lifecycle (Open → Monitoring → Resolved)
Records decision logs and action history
Appends predictions in real-time

3. CorrelationService (Incident Clustering)
Pure clustering algorithm without requiring shared incident IDs
Multi-factor similarity scoring (location, type, time, text)
Handles both new incidents and updates to existing ones
Supports incident reopening based on new evidence

4. AssessmentService (Severity Evaluation)
Rule-based severity assessment with keyword analysis
Optional LLM integration for enhanced assessment
Confidence scoring and conflict detection
Escalation from multiple weak signals

5. DecisionService (Action Selection)
Maps (severity, confidence, status) to actions
Implements duplicate-action avoidance
Human review triggering based on confidence thresholds
Service selection based on incident type

6. DashboardController (Web Interface)
Real-time dashboard for monitoring agent behavior
Step-by-step report processing
Incident filtering and action history inspection
Agentic Design
Perception (Observe)
The agent observes incoming reports by:
Normalizing missing/malformed fields
Generating fallback IDs and timestamps
Extracting structured data from unstructured text
Reasoning (Correlate → Assess → Decide)
The agent reasons through incidents using:
Correlation: Multi-factor similarity scoring
Location similarity (35% weight)
Type matching (25% weight)
Time proximity (15% weight)
Text similarity (25% weight)
Assessment: Severity determination
Keyword-based hazard detection
Confidence scoring based on signal strength
Conflict detection (e.g., "all clear" vs "fire")
Historical evidence stacking
Decision: Action selection
Service dispatch logic
Duplicate action prevention
Human review triggering
Status progression (Open → Monitoring → Resolved)
Action (Act)
The agent acts by:
Dispatching appropriate services
Recording action history
Updating incident status
Triggering human review when needed
Learning (Monitor)
The agent monitors by:
Tracking incident state changes
Reopening resolved incidents on new evidence
Maintaining decision logs for audit trails
Configuration
Thresholds and Weights
All correlation thresholds and weights are configurable in `appsettings.json`:
```json
{
  "Correlation": {
    "WeightLocation": 0.35,
    "WeightType": 0.25,
    "WeightTime": 0.15,
    "WeightText": 0.25,
    "MatchThreshold": 0.55,
    "DuplicateThreshold": 0.90,
    "TimeFullScoreHours": 6,
    "TimeZeroScoreHours": 48
  },
  "Decision": {
    "HumanReviewConfidenceFloor": 0.45
  }
}
```
Service Mappings
Service selection is keyword-based and can be extended in `DecisionService.PickService()`.
Data Flow
Input: CSV files (`campus_reports.csv`, `campus_services.csv`)
Processing: Sequential report processing through the pipeline
State: In-memory incident store with thread-safe operations
Output: `predictions.jsonl` (one line per processed report)
Installation
Prerequisites
.NET 8.0 SDK
Visual Studio 2022 or VS Code with C# extension
Setup
```bash
dotnet restore
dotnet build
dotnet run
```
The application will start on `https://localhost:5001` (or the configured port).
Usage
Web Dashboard
Navigate to the dashboard URL
Click "Process Next" to process reports one at a time
Click "Process All" to process all remaining reports
Filter action history by incident ID
Download `predictions.jsonl` from the dashboard
CSV Format
campus_reports.csv
```csv
report_id,location,type,description,reported_severity,reporter_type,timestamp
R001,Library,Fire,Smoke detected in west wing,HIGH,student,2024-01-15T10:30:00Z
```
campus_services.csv
```csv
service_id,name,type,keywords
SVC-FIRE,Fire Department,emergency,fire,smoke,flames,extinguisher
```
Testing
Unit Tests
```bash
dotnet test CampusCrisisAgent.Tests
```
Integration Tests
```bash
dotnet test CampusCrisisAgent.IntegrationTests
```
LLM Integration (Optional)
The system supports optional LLM integration for enhanced assessment:
Implement `ILlmClient` interface
Register the implementation in `Program.cs`
The `AssessmentService` will automatically use it when available
Example implementation:
```csharp
builder.Services.AddSingleton<ILlmClient, GroqLlmClient>();
```
Key Algorithms
Correlation Scoring
The correlation score is calculated as:
```
Score = (WeightLocation × LocationSimilarity) +
        (WeightType × TypeMatch) +
        (WeightTime × TimeProximity) +
        (WeightText × TextSimilarity)
```
Location Similarity
Exact match: 1.0
Substring match: 0.85
Token overlap or Levenshtein distance: calculated
Missing/unknown: 0.15 (weak prior)
Time Proximity
Within 6 hours: 1.0
Beyond 48 hours: 0.0
Linear interpolation between 6-48 hours
Severity Assessment
Rank 3 (CRITICAL): 0.9 confidence
Rank 2 (HIGH): 0.75 confidence
Rank 1 (MEDIUM): 0.65 confidence
Rank 0 (LOW): 0.7 confidence
Escalation Logic
Multiple minor reports can raise severity over time:
```
NewRank = Min(3, PriorRank + Min(2, HistoryCount/2 + SignalBoost))
```
Safety Features
Defensive Programming: All services handle malformed input gracefully
Thread Safety: Concurrent operations on shared state
Fallback Mechanisms: Rule-based assessment when LLM fails
Human Oversight: Automatic review triggering for critical/low-confidence decisions
Audit Trail: Complete decision logging for transparency

Limitations
In-Memory State: Incident state is lost on application restart
No Persistence: No database backing for long-term storage
Sequential Processing: Reports processed in file order only
Rule-Based: Default assessment relies on keyword matching
Single Instance: Not designed for distributed deployment

Future Enhancements
Database persistence for incident state
Real-time report ingestion (webhook/API)
Machine learning model for correlation
Multi-agent coordination for complex incidents
Mobile app for field responders
Integration with campus notification systems

Technical Defence
Architecture Understanding 
Clear separation of concerns across services
Well-defined data flow and state management
Configurable thresholds and weights
Extensible service and assessment plugins
Agentic Design Understanding 
Implements observe-correlate-assess-decide-act-monitor pipeline
Perception through normalized report observation
Reasoning through multi-factor correlation and assessment
Action through service dispatch and state updates
Learning through incident lifecycle management
Implementation Understanding 
Efficient algorithms (Levenshtein, token overlap)
Thread-safe concurrent operations
Real-time prediction output
Comprehensive error handling
Limitations and Testing Awareness 
Documented limitations and constraints
Unit tests for core services
Integration tests for pipeline
Test coverage reports available
Team Ownership and Contribution 
Git history for contribution tracking
CONTRIBUTING.md for collaboration guidelines
Clear code ownership through service separation
Documentation for knowledge sharing

License
This project is part of the Campus Crisis Agent assessment for DevSquad.
Contact
For questions or issues, please refer to the project repository or contact the development team.
