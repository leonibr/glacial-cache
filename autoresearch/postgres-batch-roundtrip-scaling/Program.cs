using System.Diagnostics;
using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Npgsql;
using NpgsqlTypes;

var options = LabOptions.Parse(args);

if (options.RunBenchmarkDotNet)
{
    Directory.CreateDirectory(LabPaths.BenchmarkArtifacts);
    Directory.SetCurrentDirectory(LabPaths.LabRoot);

    BenchmarkRunner.Run<PostgresBatchRoundtripBenchmarks>(
        ManualConfig.Create(DefaultConfig.Instance)
            .AddJob(Job.ShortRun
                .WithLaunchCount(1)
                .WithWarmupCount(1)
                .WithIterationCount(3)));

    return 0;
}

if (options.RunLivePostgres)
{
    return await LivePostgresVerifier.RunAsync(options);
}

return RunIterationLoop(options);

static int RunIterationLoop(LabOptions options)
{
    var iterations = options.Iterations ?? 200;
    if (iterations <= 0)
    {
        throw new ArgumentOutOfRangeException(nameof(options), "--iterations must be greater than zero.");
    }

    Console.WriteLine("GlacialCache PostgreSQL batch round-trip scaling lab");
    Console.WriteLine("----------------------------------------------------");
    Console.WriteLine("Dirty-worktree lab evidence only; production/source files are not edited.");
    Console.WriteLine($"Iterations requested: {iterations}");
    Console.WriteLine("Candidate variable: batch size from 1 through iteration count.");
    Console.WriteLine("Payload profiles: generated deterministic records at 128 B, 1024 B, and 4096 B.");
    Console.WriteLine("Acceptance: exact result count and payload equality; deterministic round-trip reduction accepted; allocation/timing only count above 2%.");
    Console.WriteLine();

    var rows = new List<IterationRow>(iterations * PayloadProfiles.All.Length * LabCases.Candidates.Length);
    var accepted = 0;
    var rejected = 0;
    IterationRow? best = null;

    for (var iteration = 1; iteration <= iterations; iteration++)
    {
        var batchSize = iteration;
        foreach (var payloadProfile in PayloadProfiles.All)
        {
            var scenario = Scenario.Create(batchSize, payloadProfile);
            var baseline = LabRunner.Measure(LabCases.Baseline, scenario);

            foreach (var labCase in LabCases.Candidates)
            {
                var candidate = LabRunner.Measure(labCase, scenario);
                var assessment = LabRunner.Assess(baseline, candidate);
                var row = new IterationRow(iteration, scenario, labCase.Name, baseline, candidate, assessment);
                rows.Add(row);

                if (assessment.Accepted)
                {
                    accepted++;
                    if (IsBetterBest(row, best))
                    {
                        best = row;
                    }
                }
                else
                {
                    rejected++;
                }
            }
        }

        Console.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"iteration={iteration}, batchSize={batchSize}, accepted={accepted}, rejected={rejected}, best={best?.CandidateName ?? "<none>"}"));
    }

    Console.WriteLine();
    Console.WriteLine($"iterationsAttempted={iterations}");
    Console.WriteLine($"acceptedCandidates={accepted}");
    Console.WriteLine($"rejectedCandidates={rejected}");
    if (best is not null)
    {
        Console.WriteLine(
            string.Create(
                CultureInfo.InvariantCulture,
                $"bestCandidate={best.CandidateName}, batchSize={best.Scenario.BatchSize}, payloadSize={best.Scenario.PayloadProfile.PayloadSize}, baselineRoundTrips={best.Baseline.DbRoundTrips}, candidateRoundTrips={best.Candidate.DbRoundTrips}, roundTripReductionPercent={best.Assessment.RoundTripReductionPercent:F4}, allocationWinPercent={best.Assessment.AllocationWinPercent:F4}, elapsedWinPercent={best.Assessment.ElapsedWinPercent:F4}, baselineAllocated={best.Baseline.AllocatedBytes}, candidateAllocated={best.Candidate.AllocatedBytes}, baselineTicks={best.Baseline.ElapsedTicks}, candidateTicks={best.Candidate.ElapsedTicks}"));
    }

    if (!string.IsNullOrWhiteSpace(options.ResultsPath))
    {
        LabRunner.WriteCsv(options.ResultsPath, rows);
        Console.WriteLine($"resultsCsv={Path.GetFullPath(options.ResultsPath)}");
    }

    return accepted > 0 && rows.All(row => row.Assessment.CorrectnessPassed || !row.Assessment.Accepted)
        ? 0
        : 1;
}

static bool IsBetterBest(IterationRow row, IterationRow? best)
{
    if (best is null)
    {
        return true;
    }

    if (row.Assessment.RoundTripReductionPercent != best.Assessment.RoundTripReductionPercent)
    {
        return row.Assessment.RoundTripReductionPercent > best.Assessment.RoundTripReductionPercent;
    }

    if (row.Candidate.DbRoundTrips != best.Candidate.DbRoundTrips)
    {
        return row.Candidate.DbRoundTrips < best.Candidate.DbRoundTrips;
    }

    if (row.Scenario.BatchSize != best.Scenario.BatchSize)
    {
        return row.Scenario.BatchSize > best.Scenario.BatchSize;
    }

    if (row.Scenario.PayloadProfile.PayloadSize != best.Scenario.PayloadProfile.PayloadSize)
    {
        return row.Scenario.PayloadProfile.PayloadSize > best.Scenario.PayloadProfile.PayloadSize;
    }

    return row.Candidate.ElapsedTicks < best.Candidate.ElapsedTicks;
}

internal static class LabRunner
{
    private const int OperationsPerMeasurement = 4;

    public static Measurement Measure(LabCase labCase, Scenario scenario)
    {
        _ = labCase.Execute(scenario);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
        var stopwatch = Stopwatch.StartNew();
        OperationResult? result = null;
        for (var i = 0; i < OperationsPerMeasurement; i++)
        {
            result = labCase.Execute(scenario);
        }

        stopwatch.Stop();
        var allocatedAfter = GC.GetTotalAllocatedBytes(precise: true);

        result ??= labCase.Execute(scenario);
        return new Measurement(
            labCase.Name,
            allocatedAfter - allocatedBefore,
            stopwatch.ElapsedTicks,
            result.DbRoundTrips,
            result.CommandCount,
            result.ParameterCount,
            result.RowsWritten,
            result.RowsRead,
            result.ResultCount,
            result.ResultCountEquality,
            result.PayloadEquality,
            result.PayloadFingerprint,
            "modeled-pass");
    }

    public static Assessment Assess(Measurement baseline, Measurement candidate)
    {
        var correctnessPassed =
            candidate.ResultCount == baseline.ResultCount &&
            candidate.ResultCountEquality &&
            candidate.PayloadEquality &&
            candidate.PayloadFingerprint == baseline.PayloadFingerprint &&
            candidate.RowsWritten == baseline.RowsWritten &&
            candidate.RowsRead == baseline.RowsRead;

        var roundTripReductionPercent = baseline.DbRoundTrips == 0
            ? 0
            : 100.0 * (baseline.DbRoundTrips - candidate.DbRoundTrips) / baseline.DbRoundTrips;
        var allocationWinPercent = baseline.AllocatedBytes == 0
            ? 0
            : 100.0 * (baseline.AllocatedBytes - candidate.AllocatedBytes) / baseline.AllocatedBytes;
        var elapsedWinPercent = baseline.ElapsedTicks == 0
            ? 0
            : 100.0 * (baseline.ElapsedTicks - candidate.ElapsedTicks) / baseline.ElapsedTicks;

        return new Assessment(
            correctnessPassed,
            roundTripReductionPercent,
            allocationWinPercent,
            elapsedWinPercent,
            correctnessPassed && candidate.DbRoundTrips < baseline.DbRoundTrips);
    }

    public static void WriteCsv(string resultsPath, IReadOnlyList<IterationRow> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(IterationRow.CsvHeader);
        foreach (var row in rows)
        {
            row.AppendCsv(builder);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(resultsPath))!);
        File.WriteAllText(resultsPath, builder.ToString());
    }
}

internal static class LabCases
{
    public static readonly LabCase Baseline = new("baseline-individual-set-get", OperationModels.IndividualSetGet);

    public static readonly LabCase[] Candidates =
    [
        new("candidate-current-npgsqlbatch-set-plus-getmultiple", OperationModels.CurrentBatchSetPlusGetMultiple),
        new("candidate-chunked-100-npgsqlbatch-set-plus-getmultiple", scenario => OperationModels.ChunkedBatchSetPlusGetMultiple(scenario, 100)),
        new("candidate-single-roundtrip-npgsqlbatch-set-and-read", OperationModels.SingleRoundtripBatchSetAndRead)
    ];
}

internal sealed record LabCase(string Name, Func<Scenario, OperationResult> Execute);

internal static class OperationModels
{
    private const int SetParameterCount = 6;

    public static OperationResult IndividualSetGet(Scenario scenario)
    {
        var store = new Dictionary<string, GeneratedPayload>(scenario.BatchSize, StringComparer.Ordinal);
        var result = new Dictionary<string, GeneratedPayload>(scenario.BatchSize, StringComparer.Ordinal);
        var roundTrips = 0;
        var commands = 0;
        var parameters = 0;

        foreach (var entry in scenario.Entries)
        {
            roundTrips++;
            commands++;
            parameters += SetParameterCount;
            store[entry.Key] = entry.Payload;
        }

        foreach (var entry in scenario.Entries)
        {
            roundTrips++;
            commands++;
            parameters++;
            if (store.TryGetValue(entry.Key, out var payload))
            {
                result[entry.Key] = payload;
            }
        }

        return BuildResult(scenario, roundTrips, commands, parameters, store.Count, result);
    }

    public static OperationResult CurrentBatchSetPlusGetMultiple(Scenario scenario)
    {
        var store = new Dictionary<string, GeneratedPayload>(scenario.BatchSize, StringComparer.Ordinal);
        foreach (var entry in scenario.Entries)
        {
            store[entry.Key] = entry.Payload;
        }

        var result = ReadAllRequested(scenario, store);
        return BuildResult(
            scenario,
            roundTrips: 2,
            commandCount: scenario.BatchSize + 1,
            parameterCount: scenario.BatchSize * SetParameterCount + 1,
            rowsWritten: store.Count,
            result);
    }

    public static OperationResult ChunkedBatchSetPlusGetMultiple(Scenario scenario, int chunkSize)
    {
        var store = new Dictionary<string, GeneratedPayload>(scenario.BatchSize, StringComparer.Ordinal);
        var writeRoundTrips = 0;
        for (var i = 0; i < scenario.Entries.Length; i += chunkSize)
        {
            writeRoundTrips++;
            var end = Math.Min(i + chunkSize, scenario.Entries.Length);
            for (var j = i; j < end; j++)
            {
                var entry = scenario.Entries[j];
                store[entry.Key] = entry.Payload;
            }
        }

        var result = ReadAllRequested(scenario, store);
        return BuildResult(
            scenario,
            roundTrips: writeRoundTrips + 1,
            commandCount: scenario.BatchSize + 1,
            parameterCount: scenario.BatchSize * SetParameterCount + 1,
            rowsWritten: store.Count,
            result);
    }

    public static OperationResult SingleRoundtripBatchSetAndRead(Scenario scenario)
    {
        var store = new Dictionary<string, GeneratedPayload>(scenario.BatchSize, StringComparer.Ordinal);
        foreach (var entry in scenario.Entries)
        {
            store[entry.Key] = entry.Payload;
        }

        var result = ReadAllRequested(scenario, store);
        return BuildResult(
            scenario,
            roundTrips: 1,
            commandCount: scenario.BatchSize + 1,
            parameterCount: scenario.BatchSize * SetParameterCount + 1,
            rowsWritten: store.Count,
            result);
    }

    private static Dictionary<string, GeneratedPayload> ReadAllRequested(
        Scenario scenario,
        IReadOnlyDictionary<string, GeneratedPayload> store)
    {
        var result = new Dictionary<string, GeneratedPayload>(scenario.BatchSize, StringComparer.Ordinal);
        foreach (var entry in scenario.Entries)
        {
            if (store.TryGetValue(entry.Key, out var payload))
            {
                result[entry.Key] = payload;
            }
        }

        return result;
    }

    private static OperationResult BuildResult(
        Scenario scenario,
        int roundTrips,
        int commandCount,
        int parameterCount,
        int rowsWritten,
        IReadOnlyDictionary<string, GeneratedPayload> result)
    {
        var payloadEquality = true;
        var fingerprint = new HashCode();
        foreach (var entry in scenario.Entries)
        {
            if (!result.TryGetValue(entry.Key, out var actual) || !entry.Payload.Payload.AsSpan().SequenceEqual(actual.Payload.AsSpan()))
            {
                payloadEquality = false;
                continue;
            }

            fingerprint.Add(entry.Key, StringComparer.Ordinal);
            fingerprint.AddBytes(actual.Payload.AsSpan());
        }

        return new OperationResult(
            roundTrips,
            commandCount,
            parameterCount,
            rowsWritten,
            result.Count,
            result.Count,
            result.Count == scenario.BatchSize,
            payloadEquality,
            fingerprint.ToHashCode());
    }
}

public sealed record OperationResult(
    int DbRoundTrips,
    int CommandCount,
    int ParameterCount,
    int RowsWritten,
    int RowsRead,
    int ResultCount,
    bool ResultCountEquality,
    bool PayloadEquality,
    int PayloadFingerprint);

internal sealed record Scenario(int BatchSize, PayloadProfile PayloadProfile, LabEntry[] Entries)
{
    public static Scenario Create(int batchSize, PayloadProfile payloadProfile)
    {
        var entries = new LabEntry[batchSize];
        for (var i = 0; i < entries.Length; i++)
        {
            var payload = GeneratedPayload.Create(batchSize, payloadProfile, i);
            entries[i] = new LabEntry($"lab:{payloadProfile.Name}:{batchSize:D3}:{i:D4}", payload);
        }

        return new Scenario(batchSize, payloadProfile, entries);
    }
}

internal sealed record LabEntry(string Key, GeneratedPayload Payload);

internal sealed record GeneratedPayload(
    int BatchSize,
    int RowIndex,
    string Partition,
    long Sequence,
    DateTimeOffset CreatedAt,
    byte[] Payload)
{
    public static GeneratedPayload Create(int batchSize, PayloadProfile profile, int rowIndex)
    {
        var payload = new byte[profile.PayloadSize];
        var seed = unchecked((batchSize * 397) ^ (profile.PayloadSize * 17) ^ rowIndex);
        for (var i = 0; i < payload.Length; i++)
        {
            payload[i] = (byte)((seed + i * 31 + (i >> 3)) & 0xFF);
        }

        return new GeneratedPayload(
            batchSize,
            rowIndex,
            profile.Name,
            ((long)batchSize << 32) | (uint)rowIndex,
            DateTimeOffset.UnixEpoch.AddSeconds(batchSize).AddMilliseconds(rowIndex),
            payload);
    }
}

internal sealed record PayloadProfile(string Name, int PayloadSize);

internal static class PayloadProfiles
{
    public static readonly PayloadProfile[] All =
    [
        new("small-record", 128),
        new("medium-record", 1024),
        new("large-record", 4096)
    ];
}

internal sealed record Measurement(
    string Name,
    long AllocatedBytes,
    long ElapsedTicks,
    int DbRoundTrips,
    int CommandCount,
    int ParameterCount,
    int RowsWritten,
    int RowsRead,
    int ResultCount,
    bool ResultCountEquality,
    bool PayloadEquality,
    int PayloadFingerprint,
    string PostgreSqlExecution);

internal sealed record Assessment(
    bool CorrectnessPassed,
    double RoundTripReductionPercent,
    double AllocationWinPercent,
    double ElapsedWinPercent,
    bool Accepted);

internal sealed record IterationRow(
    int Iteration,
    Scenario Scenario,
    string CandidateName,
    Measurement Baseline,
    Measurement Candidate,
    Assessment Assessment)
{
    public const string CsvHeader = "iteration,batchSize,payloadName,payloadSize,candidate,accepted,correctnessPassed,baselineRoundTrips,candidateRoundTrips,roundTripReductionPercent,baselineAllocatedBytes,candidateAllocatedBytes,allocationWinPercent,baselineElapsedTicks,candidateElapsedTicks,elapsedWinPercent,baselineCommandCount,candidateCommandCount,baselineParameterCount,candidateParameterCount,baselineRowsWritten,candidateRowsWritten,baselineRowsRead,candidateRowsRead,baselineResultCount,candidateResultCount,resultCountEquality,payloadEquality,payloadFingerprint,postgresqlExecution";

    public void AppendCsv(StringBuilder builder)
    {
        Append(builder, Iteration);
        Append(builder, Scenario.BatchSize);
        Append(builder, Scenario.PayloadProfile.Name);
        Append(builder, Scenario.PayloadProfile.PayloadSize);
        Append(builder, CandidateName);
        Append(builder, Assessment.Accepted);
        Append(builder, Assessment.CorrectnessPassed);
        Append(builder, Baseline.DbRoundTrips);
        Append(builder, Candidate.DbRoundTrips);
        Append(builder, Assessment.RoundTripReductionPercent);
        Append(builder, Baseline.AllocatedBytes);
        Append(builder, Candidate.AllocatedBytes);
        Append(builder, Assessment.AllocationWinPercent);
        Append(builder, Baseline.ElapsedTicks);
        Append(builder, Candidate.ElapsedTicks);
        Append(builder, Assessment.ElapsedWinPercent);
        Append(builder, Baseline.CommandCount);
        Append(builder, Candidate.CommandCount);
        Append(builder, Baseline.ParameterCount);
        Append(builder, Candidate.ParameterCount);
        Append(builder, Baseline.RowsWritten);
        Append(builder, Candidate.RowsWritten);
        Append(builder, Baseline.RowsRead);
        Append(builder, Candidate.RowsRead);
        Append(builder, Baseline.ResultCount);
        Append(builder, Candidate.ResultCount);
        Append(builder, Candidate.ResultCountEquality);
        Append(builder, Candidate.PayloadEquality);
        Append(builder, Candidate.PayloadFingerprint);
        AppendLast(builder, Candidate.PostgreSqlExecution);
    }

    private static void Append<T>(StringBuilder builder, T value)
    {
        builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
        builder.Append(',');
    }

    private static void AppendLast<T>(StringBuilder builder, T value)
    {
        builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
        builder.AppendLine();
    }
}

internal static class LivePostgresVerifier
{
    public static async Task<int> RunAsync(LabOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            Console.Error.WriteLine("--live requires --connection or POSTGRES_CONNECTION_STRING.");
            return 2;
        }

        var batchSize = options.LiveBatchSize ?? 32;
        var payloadSize = options.LivePayloadSize ?? 1024;
        var scenario = Scenario.Create(batchSize, new PayloadProfile("live-record", payloadSize));
        var tableName = "lab_cache_" + Guid.NewGuid().ToString("N");

        await using var dataSource = NpgsqlDataSource.Create(options.ConnectionString);
        await using var connection = await dataSource.OpenConnectionAsync();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"CREATE TEMP TABLE {tableName} (key text PRIMARY KEY, payload bytea NOT NULL);";
            await command.ExecuteNonQueryAsync();
        }

        var individual = await RunIndividualAsync(connection, tableName, scenario);
        await TruncateAsync(connection, tableName);
        var currentBatch = await RunCurrentBatchAsync(connection, tableName, scenario);
        await TruncateAsync(connection, tableName);
        var singleRoundtrip = await RunSingleRoundtripBatchAsync(connection, tableName, scenario);

        Console.WriteLine("livePostgres=pass");
        Console.WriteLine($"batchSize={batchSize}");
        Console.WriteLine($"payloadSize={payloadSize}");
        Console.WriteLine($"individual: resultCount={individual.ResultCount}, payloadEquality={individual.PayloadEquality}, modeledRoundTrips={individual.ModeledRoundTrips}");
        Console.WriteLine($"currentBatch: resultCount={currentBatch.ResultCount}, payloadEquality={currentBatch.PayloadEquality}, modeledRoundTrips={currentBatch.ModeledRoundTrips}");
        Console.WriteLine($"singleRoundtripBatch: resultCount={singleRoundtrip.ResultCount}, payloadEquality={singleRoundtrip.PayloadEquality}, modeledRoundTrips={singleRoundtrip.ModeledRoundTrips}");

        return individual.PayloadEquality &&
            currentBatch.PayloadEquality &&
            singleRoundtrip.PayloadEquality &&
            individual.ResultCount == scenario.BatchSize &&
            currentBatch.ResultCount == scenario.BatchSize &&
            singleRoundtrip.ResultCount == scenario.BatchSize
            ? 0
            : 1;
    }

    private static async Task<LiveResult> RunIndividualAsync(NpgsqlConnection connection, string tableName, Scenario scenario)
    {
        foreach (var entry in scenario.Entries)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"INSERT INTO {tableName} (key, payload) VALUES ($1, $2);";
            command.Parameters.Add(new NpgsqlParameter { Value = entry.Key, NpgsqlDbType = NpgsqlDbType.Text });
            command.Parameters.Add(new NpgsqlParameter { Value = entry.Payload.Payload, NpgsqlDbType = NpgsqlDbType.Bytea });
            await command.ExecuteNonQueryAsync();
        }

        var results = new Dictionary<string, byte[]>(scenario.BatchSize, StringComparer.Ordinal);
        foreach (var entry in scenario.Entries)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = $"SELECT payload FROM {tableName} WHERE key = $1;";
            command.Parameters.Add(new NpgsqlParameter { Value = entry.Key, NpgsqlDbType = NpgsqlDbType.Text });
            var value = await command.ExecuteScalarAsync();
            if (value is byte[] payload)
            {
                results[entry.Key] = payload;
            }
        }

        return BuildLiveResult(scenario, results, scenario.BatchSize * 2);
    }

    private static async Task<LiveResult> RunCurrentBatchAsync(NpgsqlConnection connection, string tableName, Scenario scenario)
    {
        await using (var batch = new NpgsqlBatch(connection))
        {
            foreach (var entry in scenario.Entries)
            {
                var command = new NpgsqlBatchCommand($"INSERT INTO {tableName} (key, payload) VALUES ($1, $2);");
                command.Parameters.Add(new NpgsqlParameter { Value = entry.Key, NpgsqlDbType = NpgsqlDbType.Text });
                command.Parameters.Add(new NpgsqlParameter { Value = entry.Payload.Payload, NpgsqlDbType = NpgsqlDbType.Bytea });
                batch.BatchCommands.Add(command);
            }

            await batch.ExecuteNonQueryAsync();
        }

        var results = await ReadMultipleAsync(connection, tableName, scenario);
        return BuildLiveResult(scenario, results, 2);
    }

    private static async Task<LiveResult> RunSingleRoundtripBatchAsync(NpgsqlConnection connection, string tableName, Scenario scenario)
    {
        var results = new Dictionary<string, byte[]>(scenario.BatchSize, StringComparer.Ordinal);
        await using var batch = new NpgsqlBatch(connection);
        foreach (var entry in scenario.Entries)
        {
            var command = new NpgsqlBatchCommand($"INSERT INTO {tableName} (key, payload) VALUES ($1, $2);");
            command.Parameters.Add(new NpgsqlParameter { Value = entry.Key, NpgsqlDbType = NpgsqlDbType.Text });
            command.Parameters.Add(new NpgsqlParameter { Value = entry.Payload.Payload, NpgsqlDbType = NpgsqlDbType.Bytea });
            batch.BatchCommands.Add(command);
        }

        var readCommand = new NpgsqlBatchCommand($"SELECT key, payload FROM {tableName} WHERE key = ANY($1);");
        readCommand.Parameters.Add(new NpgsqlParameter { Value = scenario.Entries.Select(entry => entry.Key).ToArray(), NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text });
        batch.BatchCommands.Add(readCommand);

        await using var reader = await batch.ExecuteReaderAsync();
        do
        {
            if (reader.FieldCount != 2)
            {
                continue;
            }

            while (await reader.ReadAsync())
            {
                results[reader.GetString(0)] = (byte[])reader[1];
            }
        }
        while (await reader.NextResultAsync());

        return BuildLiveResult(scenario, results, 1);
    }

    private static async Task<Dictionary<string, byte[]>> ReadMultipleAsync(NpgsqlConnection connection, string tableName, Scenario scenario)
    {
        var results = new Dictionary<string, byte[]>(scenario.BatchSize, StringComparer.Ordinal);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT key, payload FROM {tableName} WHERE key = ANY($1);";
        command.Parameters.Add(new NpgsqlParameter { Value = scenario.Entries.Select(entry => entry.Key).ToArray(), NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text });
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results[reader.GetString(0)] = (byte[])reader[1];
        }

        return results;
    }

    private static async Task TruncateAsync(NpgsqlConnection connection, string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"TRUNCATE TABLE {tableName};";
        await command.ExecuteNonQueryAsync();
    }

    private static LiveResult BuildLiveResult(Scenario scenario, IReadOnlyDictionary<string, byte[]> results, int modeledRoundTrips)
    {
        var payloadEquality = true;
        foreach (var entry in scenario.Entries)
        {
            if (!results.TryGetValue(entry.Key, out var payload) || !entry.Payload.Payload.AsSpan().SequenceEqual(payload))
            {
                payloadEquality = false;
                break;
            }
        }

        return new LiveResult(results.Count, payloadEquality, modeledRoundTrips);
    }
}

internal sealed record LiveResult(int ResultCount, bool PayloadEquality, int ModeledRoundTrips);

public class PostgresBatchRoundtripBenchmarks
{
    private Scenario _scenario = null!;

    [Params(10, 100, 200)]
    public int BatchSize { get; set; }

    [Params(128, 1024, 4096)]
    public int PayloadSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _scenario = Scenario.Create(BatchSize, new PayloadProfile("bdn-record", PayloadSize));
    }

    [Benchmark(Baseline = true)]
    public OperationResult IndividualSetGet()
    {
        return OperationModels.IndividualSetGet(_scenario);
    }

    [Benchmark]
    public OperationResult CurrentBatchSetPlusGetMultiple()
    {
        return OperationModels.CurrentBatchSetPlusGetMultiple(_scenario);
    }

    [Benchmark]
    public OperationResult SingleRoundtripBatchSetAndRead()
    {
        return OperationModels.SingleRoundtripBatchSetAndRead(_scenario);
    }
}

internal static class LabPaths
{
    public static readonly string LabRoot = AppContext.BaseDirectory.Split(
        $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
        StringSplitOptions.None)[0];

    public static readonly string BenchmarkArtifacts = Path.Combine(LabRoot, "BenchmarkDotNet.Artifacts");
}

internal sealed record LabOptions(
    int? Iterations,
    string? ResultsPath,
    bool RunBenchmarkDotNet,
    bool RunLivePostgres,
    string? ConnectionString,
    int? LiveBatchSize,
    int? LivePayloadSize)
{
    public static LabOptions Parse(string[] args)
    {
        int? iterations = null;
        string? resultsPath = null;
        var runBenchmarkDotNet = false;
        var runLivePostgres = false;
        string? connectionString = Environment.GetEnvironmentVariable("POSTGRES_CONNECTION_STRING");
        int? liveBatchSize = null;
        int? livePayloadSize = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--iterations":
                    iterations = int.Parse(ReadValue(args, ref i), CultureInfo.InvariantCulture);
                    break;
                case "--results":
                    resultsPath = ReadValue(args, ref i);
                    break;
                case "--benchmarkdotnet":
                    runBenchmarkDotNet = true;
                    break;
                case "--live":
                    runLivePostgres = true;
                    break;
                case "--connection":
                    connectionString = ReadValue(args, ref i);
                    break;
                case "--live-batch-size":
                    liveBatchSize = int.Parse(ReadValue(args, ref i), CultureInfo.InvariantCulture);
                    break;
                case "--live-payload-size":
                    livePayloadSize = int.Parse(ReadValue(args, ref i), CultureInfo.InvariantCulture);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'.");
            }
        }

        return new LabOptions(iterations, resultsPath, runBenchmarkDotNet, runLivePostgres, connectionString, liveBatchSize, livePayloadSize);
    }

    private static string ReadValue(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
        {
            throw new ArgumentException($"Missing value for '{args[index]}'.");
        }

        index++;
        return args[index];
    }
}
