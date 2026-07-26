using System.Text.Json;
using Autoresearch.Contract;

var labRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
var artifactPath = Path.Combine(labRoot, "artifacts", "evaluation.json");
Directory.CreateDirectory(Path.GetDirectoryName(artifactPath)!);

EvaluatorManifest? manifest = null;
try
{
    manifest = JsonSerializer.Deserialize<EvaluatorManifest>(
        await File.ReadAllTextAsync(Path.Combine(labRoot, "lab.manifest.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
}
catch (Exception exception) when (exception is IOException or JsonException)
{
    // The stable failure below deliberately does not expose local paths or exception text.
}

if (manifest is null || !manifest.ContractConfigured || IsUnconfigured(manifest.BaseCommit) ||
    IsUnconfigured(manifest.SeriesId) || manifest.Metric is null || IsUnconfigured(manifest.Metric.Name) ||
    !StringComparer.Ordinal.Equals(manifest.Metric.Direction, "maximize"))
{
    var unconfigured = new EvaluationReport(
        "invalid", "contract_not_configured", "maximize", 0, 0, 0, 0,
        [], [new GateResult("contract_configured", false, "Replace the example contract, workload, oracle, and score.")],
        "replace_example_evaluator", "unconfigured", "unconfigured", "unknown", "unconfigured", "unconfigured");
    await File.WriteAllTextAsync(artifactPath, JsonSerializer.Serialize(unconfigured, JsonOptions));
    Console.WriteLine("status=fail score=0 metric=contract_not_configured direction=maximize reason=replace_example_evaluator");
    return 3;
}

// Replace this branch with the frozen production-shaped workload. It intentionally cannot
// award a score, so copying the template without configuring the evaluator fails closed.
var report = new EvaluationReport(
    "invalid", "evaluator_not_implemented", "maximize", 0, 0, 0, 0,
    [], [new GateResult("evaluator_implemented", false, "Implement paired randomized A/B trials and semantic gates.")],
    "implement_frozen_evaluator", manifest.SeriesId, "unconfigured", "unknown", manifest.BaseCommit, "unconfigured");
await File.WriteAllTextAsync(artifactPath, JsonSerializer.Serialize(report, JsonOptions));
Console.WriteLine("status=fail score=0 metric=evaluator_not_implemented direction=maximize reason=implement_frozen_evaluator");
return 3;

internal static partial class Program
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static bool IsUnconfigured(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Contains("{{", StringComparison.Ordinal) ||
        StringComparer.Ordinal.Equals(value, "unconfigured_for_scaffold");
}

internal sealed record EvaluatorManifest(bool ContractConfigured, string BaseCommit, string SeriesId, EvaluatorMetric? Metric);

internal sealed record EvaluatorMetric(string Name, string Direction);
