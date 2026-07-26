using System.Text.Json;
using System.Text.RegularExpressions;
using Autoresearch.Contract;

const int Keep = 0;
const int Reject = 1;
const int BehavioralFailure = 2;
const int Invalid = 3;
const int InfrastructureFailure = 4;
const int ProtectedPathViolation = 5;

if (args.Length != 4 || args.Any(static path => !File.Exists(path)))
{
    Console.Error.WriteLine("decision=infra reason=missing_gate_input");
    return InfrastructureFailure;
}

var changedPaths = File.ReadLines(args[1])
    .Select(static line => line.Trim().Replace('\\', '/'))
    .Where(static line => line.Length > 0)
    .ToArray();
if (changedPaths.Length != 1 || !StringComparer.Ordinal.Equals(changedPaths[0], "Candidate/Candidate.cs"))
{
    Console.Error.WriteLine($"decision=reject reason=protected_path_violation paths={string.Join(',', changedPaths)}");
    return ProtectedPathViolation;
}

EvaluationReport? report;
GateManifest? manifest;
AuthorityContext? authority;
try
{
    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    report = JsonSerializer.Deserialize<EvaluationReport>(await File.ReadAllTextAsync(args[0]), options);
    manifest = JsonSerializer.Deserialize<GateManifest>(await File.ReadAllTextAsync(args[2]), options);
    authority = JsonSerializer.Deserialize<AuthorityContext>(await File.ReadAllTextAsync(args[3]), options);
}
catch (Exception exception) when (exception is IOException or JsonException)
{
    Console.Error.WriteLine("decision=infra reason=unreadable_gate_input");
    return InfrastructureFailure;
}

if (!ValidManifest(manifest) || !ValidAuthority(authority))
{
    Console.WriteLine("decision=invalid reason=unconfigured_or_invalid_protected_expectations");
    return Invalid;
}

if (!ValidReportShape(report) ||
    !StringComparer.Ordinal.Equals(report!.Metric, manifest!.Metric!.Name) ||
    !StringComparer.Ordinal.Equals(report.Direction, "maximize") ||
    !StringComparer.Ordinal.Equals(report.BaseCommit, manifest.BaseCommit) ||
    !StringComparer.Ordinal.Equals(report.SeriesId, manifest.SeriesId) ||
    !StringComparer.Ordinal.Equals(report.CandidateCommit, authority!.CandidateCommit) ||
    !StringComparer.Ordinal.Equals(report.EnvironmentFingerprint, authority.EnvironmentFingerprint) ||
    !StringComparer.Ordinal.Equals(report.Tier, authority.Tier) ||
    !CompleteTrialMatrix(report.Trials, manifest.RequiredScenarios!, RequiredPairs(manifest.Trials!, authority.Tier)) ||
    !RequiredGatesMatch(report.Gates, manifest.RequiredGates!))
{
    Console.WriteLine("decision=invalid reason=report_does_not_match_protected_expectations");
    return Invalid;
}

if (report.Gates.Any(static gate => !gate.Passed))
{
    Console.WriteLine("decision=reject reason=behavioral_gate_failed");
    return BehavioralFailure;
}

if (!StringComparer.Ordinal.Equals(report.Status, "pass") || !string.IsNullOrWhiteSpace(report.Reason))
{
    Console.WriteLine("decision=invalid reason=incomparable_result");
    return Invalid;
}

if (report.ConfidenceIntervalLow <= 0 ||
    report.WorstScenarioRegressionPercent > manifest.Metric.MaximumWorstScenarioRegressionPercent)
{
    Console.WriteLine("decision=reject reason=insufficient_or_unsafe_improvement");
    return Reject;
}

Console.WriteLine(FormattableString.Invariant($"decision=keep score={report.Score:F6} metric={report.Metric}"));
return Keep;

static bool ValidManifest(GateManifest? value) =>
    value is not null && value.ContractConfigured && value.Metric is not null && value.Trials is not null &&
    value.RequiredGates is { Count: > 0 } && value.RequiredScenarios is { Count: > 0 } &&
    !Unconfigured(value.TargetSlice) && ValidSha(value.BaseCommit) &&
    !Unconfigured(value.SeriesId) && Regex.IsMatch(value.SeriesId, "^[a-zA-Z0-9][a-zA-Z0-9._-]*$") &&
    !Unconfigured(value.Metric.Name) && Regex.IsMatch(value.Metric.Name, "^[a-z][a-z0-9_]*$") &&
    StringComparer.Ordinal.Equals(value.Metric.Direction, "maximize") &&
    value.Metric.ConfidenceIntervalMustExcludeZero &&
    double.IsFinite(value.Metric.MaximumWorstScenarioRegressionPercent) &&
    value.Metric.MaximumWorstScenarioRegressionPercent >= 0 &&
    value.Trials.Design == "paired-randomized-ab" && value.Trials.Seed >= 0 &&
    value.Trials.ScoutPairs > 0 && value.Trials.PromotionPairs >= value.Trials.ScoutPairs &&
    value.Trials.ReproductionPairs >= value.Trials.PromotionPairs &&
    value.RequiredGates.All(static gate => !Unconfigured(gate) && Regex.IsMatch(gate, "^[a-z][a-z0-9_]*$")) &&
    value.RequiredGates.Distinct(StringComparer.Ordinal).Count() == value.RequiredGates.Count &&
    value.RequiredScenarios.All(static scenario => !Unconfigured(scenario)) &&
    value.RequiredScenarios.Distinct(StringComparer.Ordinal).Count() == value.RequiredScenarios.Count;

static bool ValidAuthority(AuthorityContext? value) =>
    value is not null && ValidSha(value.CandidateCommit) && !Unconfigured(value.EnvironmentFingerprint) &&
    value.EnvironmentFingerprint.Length >= 16 && value.Tier is "scout" or "promotion" or "reproduction";

static bool ValidReportShape(EvaluationReport? value) =>
    value is not null && value.Trials is { Count: > 0 } && value.Gates is { Count: > 0 } &&
    value.Status is not null && value.Reason is not null &&
    double.IsFinite(value.Score) && double.IsFinite(value.ConfidenceIntervalLow) &&
    double.IsFinite(value.ConfidenceIntervalHigh) && value.ConfidenceIntervalLow <= value.ConfidenceIntervalHigh &&
    double.IsFinite(value.WorstScenarioRegressionPercent) && value.WorstScenarioRegressionPercent >= 0 &&
    ValidSha(value.BaseCommit) && ValidSha(value.CandidateCommit) && !Unconfigured(value.EnvironmentFingerprint) &&
    value.Trials.All(static trial => !Unconfigured(trial.Scenario) && trial.PairIndex > 0 &&
        double.IsFinite(trial.Baseline) && trial.Baseline > 0 && double.IsFinite(trial.Candidate) && trial.Candidate > 0) &&
    value.Trials.Select(static trial => (trial.Scenario, trial.PairIndex)).Distinct().Count() == value.Trials.Count &&
    value.Gates.All(static gate => !Unconfigured(gate.Name) && !Unconfigured(gate.Evidence)) &&
    value.Gates.Select(static gate => gate.Name).Distinct(StringComparer.Ordinal).Count() == value.Gates.Count;

static bool RequiredGatesMatch(IReadOnlyList<GateResult> actual, IReadOnlyList<string> required)
{
    var actualNames = actual.Select(static gate => gate.Name).Order(StringComparer.Ordinal).ToArray();
    var requiredNames = required.Order(StringComparer.Ordinal).ToArray();
    return actualNames.SequenceEqual(requiredNames, StringComparer.Ordinal);
}

static bool CompleteTrialMatrix(
    IReadOnlyList<TrialResult> trials,
    IReadOnlyList<string> requiredScenarios,
    int requiredPairs)
{
    if (requiredPairs <= 0 || trials.Count != requiredPairs * requiredScenarios.Count)
    {
        return false;
    }

    var expectedScenarios = requiredScenarios.ToHashSet(StringComparer.Ordinal);
    var actualPairIndices = trials.Select(static trial => trial.PairIndex).ToHashSet();
    if (!actualPairIndices.SetEquals(Enumerable.Range(1, requiredPairs)))
    {
        return false;
    }

    return Enumerable.Range(1, requiredPairs).All(pairIndex =>
    {
        var scenariosForPair = trials
            .Where(trial => trial.PairIndex == pairIndex)
            .Select(static trial => trial.Scenario)
            .ToArray();
        return scenariosForPair.Length == requiredScenarios.Count &&
            scenariosForPair.ToHashSet(StringComparer.Ordinal).SetEquals(expectedScenarios);
    });
}

static int RequiredPairs(GateTrials trials, string tier) => tier switch
{
    "scout" => trials.ScoutPairs,
    "promotion" => trials.PromotionPairs,
    "reproduction" => trials.ReproductionPairs,
    _ => int.MaxValue
};

static bool ValidSha(string? value) =>
    value is not null && (value.Length is 40 or 64) && value.All(static character => char.IsAsciiHexDigit(character));

static bool Unconfigured(string? value) =>
    string.IsNullOrWhiteSpace(value) || value.Contains("{{", StringComparison.Ordinal) ||
    StringComparer.Ordinal.Equals(value, "unconfigured_for_scaffold");

internal sealed record GateManifest(
    bool ContractConfigured,
    string TargetSlice,
    string BaseCommit,
    string SeriesId,
    GateMetric? Metric,
    GateTrials? Trials,
    IReadOnlyList<string>? RequiredGates,
    IReadOnlyList<string>? RequiredScenarios);

internal sealed record GateMetric(
    string Name,
    string Direction,
    bool ConfidenceIntervalMustExcludeZero,
    double MaximumWorstScenarioRegressionPercent);

internal sealed record GateTrials(string Design, int Seed, int ScoutPairs, int PromotionPairs, int ReproductionPairs);

internal sealed record AuthorityContext(string CandidateCommit, string EnvironmentFingerprint, string Tier);
