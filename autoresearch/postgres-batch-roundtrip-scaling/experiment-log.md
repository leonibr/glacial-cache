# PostgreSQL Batch Round-Trip Scaling Lab

Date: 2026-07-25

Scope: lab-only PostgreSQL batch operation round-trip scaling under `autoresearch/postgres-batch-roundtrip-scaling`.

Dirty-worktree context recorded before starting: production/source/docs/root lock files had unrelated modifications. This lab does not treat current production state as a clean baseline and does not edit production/source files.

Acceptance rules:

- Result counts and payload equality must hold exactly.
- Deterministic round-trip reductions may be accepted when modeled or live verified and labeled.
- Allocation and timing wins only count when greater than 2%.
- Timing-only noise is not accepted.
- BenchmarkDotNet artifacts must stay under this lab folder.

Initial baseline intent:

- Compare individual PostgreSQL command shape against current batch-shaped set plus `GetMultiple` read.
- Include larger batch-size scaling than the production benchmark's 10, 50, 100, 500 matrix by sweeping batch sizes 1 through 200.
- Include fixed generated payload profiles at 128 B, 1 KiB, and 4 KiB so batch-size scaling is the iteration variable while payload-size coverage remains explicit.
- Record modeled DB round trips, command count, parameter count, rows written/read, result count equality, payload equality, elapsed ticks, and allocated bytes.

## Results

Commands completed:

- `dotnet build C:\projetos\github\glacial-cache\autoresearch\postgres-batch-roundtrip-scaling\PostgresBatchRoundtripScalingLab.csproj -c Release`
- `dotnet run -c Release --project C:\projetos\github\glacial-cache\autoresearch\postgres-batch-roundtrip-scaling\PostgresBatchRoundtripScalingLab.csproj -- --iterations 200 --results C:\projetos\github\glacial-cache\autoresearch\postgres-batch-roundtrip-scaling\iteration-results.csv`
- `dotnet run -c Release --project C:\projetos\github\glacial-cache\autoresearch\postgres-batch-roundtrip-scaling\PostgresBatchRoundtripScalingLab.csproj -- --benchmarkdotnet`
- `docker run --rm -d --name glacial-postgres-batch-roundtrip-lab -e POSTGRES_USER=labuser -e POSTGRES_PASSWORD=labpass -e POSTGRES_DB=labdb -p 55440:5432 postgres:17-alpine`
- `docker exec glacial-postgres-batch-roundtrip-lab pg_isready -U labuser -d labdb`
- `dotnet run -c Release --project C:\projetos\github\glacial-cache\autoresearch\postgres-batch-roundtrip-scaling\PostgresBatchRoundtripScalingLab.csproj -- --live --connection Host=localhost;Port=55440;Database=labdb;Username=labuser;Password=labpass --live-batch-size 32 --live-payload-size 1024`
- `docker stop glacial-postgres-batch-roundtrip-lab`

Deterministic 200-iteration loop:

- Iterations attempted: 200.
- Candidate rows: 1,800 across 3 candidates and 3 fixed payload profiles.
- Accepted candidates: 1,794.
- Rejected candidates: 6.
- Rejected rows were only batch-size 1 rows for current batch and chunked batch because baseline individual set plus get also has 2 modeled round trips, so no round-trip reduction existed.
- Correctness: accepted rows preserved exact result count, rows written/read, and payload fingerprint equality.

Baseline metrics for best row:

- Baseline: `baseline-individual-set-get`.
- Batch size: 200.
- Payload: `large-record`, 4,096 B per row.
- Rows written/read/result count: 200 / 200 / 200.
- DB round trips: 400.
- Command count: 400.
- Parameter count: 1,400.
- Allocated bytes: 55,120.
- Elapsed ticks: 7,100.

Best candidate:

- Candidate: `candidate-single-roundtrip-npgsqlbatch-set-and-read`.
- Batch size: 200.
- Payload: `large-record`, 4,096 B per row.
- Rows written/read/result count: 200 / 200 / 200.
- DB round trips: 1.
- Command count: 201.
- Parameter count: 1,201.
- Round-trip reduction: 99.75%.
- Allocated bytes: 55,120; allocation win 0%, not accepted as an allocation win.
- Elapsed ticks: 7,147; elapsed movement -0.6620%, not accepted as a timing win.

Other deterministic comparison at batch size 200 and 4,096 B payload:

- `candidate-current-npgsqlbatch-set-plus-getmultiple`: 400 to 2 modeled round trips, 99.5% reduction, correctness passed.
- `candidate-chunked-100-npgsqlbatch-set-plus-getmultiple`: 400 to 3 modeled round trips, 99.25% reduction, correctness passed.

BenchmarkDotNet:

- Used ShortRun against model-only `BatchSize` 10, 100, 200 and payload size 128, 1,024, 4,096.
- Artifacts are lab-local under `C:\projetos\github\glacial-cache\autoresearch\postgres-batch-roundtrip-scaling\BenchmarkDotNet.Artifacts`.
- Results are useful as allocation/timing context only. Several confidence intervals are too wide for timing-only acceptance, so no timing-only claim is made.

Live PostgreSQL:

- Docker image `postgres:17-alpine` was already local and Docker was available.
- Bounded live run passed at batch size 32 and payload size 1,024 B.
- Live equality: individual, current batch plus `GetMultiple`, and single-roundtrip `NpgsqlBatch` set-and-read all returned 32 results with exact payload equality.
- Modeled round trips for the live scenario: individual 64, current batch plus read 2, single-roundtrip batch 1.

Conclusion:

- Best lab-only candidate is `candidate-single-roundtrip-npgsqlbatch-set-and-read` because it keeps correctness exact and deterministically reduces modeled PostgreSQL client/server exchanges from 400 to 1 at batch size 200.
- Existing current batch shape already gives the main production-known win versus individual commands for batch sizes greater than 1: 2 round trips instead of 2N.
- This lab does not claim production behavior is fixed. Production follow-up should separately characterize whether combining write batch and read result in one `NpgsqlBatch` is useful for real workflows, transaction semantics, result streaming, failure behavior, logging, and public API shape.
