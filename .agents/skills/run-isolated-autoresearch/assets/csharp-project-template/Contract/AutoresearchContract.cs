namespace Autoresearch.Contract;

// Replace these example records before freezing the lab. Workers cannot edit this project.
public sealed record CandidateInput(string Value);

public sealed record CandidateResult(string Value);

public sealed record TrialResult(string Scenario, int PairIndex, double Baseline, double Candidate);

public sealed record GateResult(string Name, bool Passed, string Evidence);

public sealed record EvaluationReport(
    string Status,
    string Metric,
    string Direction,
    double Score,
    double ConfidenceIntervalLow,
    double ConfidenceIntervalHigh,
    double WorstScenarioRegressionPercent,
    IReadOnlyList<TrialResult> Trials,
    IReadOnlyList<GateResult> Gates,
    string Reason,
    string SeriesId,
    string Tier,
    string CandidateCommit,
    string BaseCommit,
    string EnvironmentFingerprint);
