# Isolated autoresearch lab contract

## Eligibility gate

Proceed only when every answer is yes:

- Does the slice represent a real or planned production behavior with user value?
- Are there multiple plausible implementation hypotheses rather than one known discrete fix?
- Can an evaluator freeze representative inputs, observable semantics, and a relevant current baseline?
- Can each experiment run repeatedly within the same affordable wall-clock budget?
- Can one scalar score reflect real measured behavior while correctness remains a hard gate?

Reject the proposal when it relies on modeled metrics, has a predetermined winner, calls workload sizes “iterations,” or lacks a real dependency required to measure the claim. Preserve an ordinary benchmark or feasibility proof under an accurate name when that is all the evidence supports.

## Narrow the semantic slice

Document:

- entry point, callers, inputs, outputs, side effects, and lifecycle;
- success, failure, cancellation, transaction, concurrency, and cleanup behavior;
- production baseline and why it is the relevant comparator;
- hypotheses that can vary without changing required semantics;
- exclusions that keep the slice bounded.

Prefer one end-to-end behavior over a broad subsystem.

## Worktree and lab boundary

Resolve and record an explicit base commit. Create a dedicated branch and worktree after inspecting existing changes. Never clean, reset, stash, move, or overwrite unrelated user work.

If dirty or untracked changes overlap target or baseline paths, stop and require an explicit user choice: exclude them and use the recorded base, include them through a user-approved snapshot/commit, or choose another base. Record that provenance. Never silently omit changes from the lab or copy them into it.

Preserve existing and untracked labs. Create a sibling isolated lab by default. Import an old artifact only with its origin and modifications recorded; never overwrite the old lab or compare its scores with the new frozen evaluator.

Place the lab at `autoresearch/<slice>-autoresearch/`. Keep the evaluator, fixtures, setup, and scoring protected. Expose one mutable `Candidate` file when feasible; otherwise enumerate the exact mutable paths. Add a lab-local `.gitignore` for `run.log`, build output, generated data, credential files, and `results.tsv` when the frozen ledger policy keeps it untracked.

For C#/.NET slices, use `assets/csharp-project-template/` as the default starting layout. Copy it into the new lab, rename `results.tsv.template` to `results.tsv`, replace all tokens documented in `template-tokens.md`, and adapt the protected `Contract` interface and evaluator before the baseline. The bundled evaluator deliberately emits `status=fail` while the manifest's single authoritative `contractConfigured` flag is false or its protected fields remain unconfigured; never flip it to true until the example contract has been replaced by the frozen production-shaped workload, oracle, paired trials, gates, and score. `run.log`, generated artifacts, and the research ledger remain ignored by default.

The protected `Gate` is separate from the evaluator and has stable exits: 0 keep, 1 reject, 2 behavioral failure, 3 invalid/incomparable, 4 infrastructure failure, and 5 protected-path violation. The default win requires a paired improvement confidence interval excluding zero and no scenario regression above 5%. Freeze a different policy before the baseline if the slice requires one.

Before each run, fail if a committed experiment changes a protected path. Do not give the experimenter write access to the score formula, gates, workload, baseline, seed, timing, or result parser.

Run canonical evaluation and gating in an Evaluation Authority boundary where workers cannot write the manifest, authority context, changed-path list, evaluation report, gate binary, or retained-best state. Bind the report's candidate SHA and environment fingerprint to authority-owned expected values. The bundled files validate these bindings but cannot establish an operating-system permission boundary by themselves.

## Freeze the evaluator

Freeze these values in `program.md` before taking the baseline:

- production-shaped workload and baseline provenance: source reference, built package, or faithful extraction, including why that representation is valid;
- deterministic seed and representative scenario set;
- scenario duration, trial duration, confirmation trial count, and equal total experiment budget;
- hard process timeout;
- one primary scalar metric and direction;
- minimum improvement above measured noise;
- external service versions, schema, configuration, dependency locks, and immutable container image digests;
- hard gates for all observable semantics.

Protect the chosen baseline representation from candidate mutation. Use live dependencies when the hypothesis concerns a database, network, filesystem, runtime, or other external behavior. Resolve mutable container tags to immutable digests before the baseline and record the resolution.

Choose and freeze an instrumentation method that directly supports each claim. Database statement counts, socket writes, protocol `Sync` messages, and `ReadyForQuery` messages are not interchangeable definitions of a round trip. State what is observed and why it represents the claimed metric; do not require a TCP parser when provider tracing or another suitable method answers the question. Never assign protocol facts as constants. Keep credentials outside tracked files and redact logs.

Workload sweeps are scenario coverage inside one evaluation. They are not successive experiments because the implementation did not change.

## Score and gates

Emit one parseable summary line:

```text
status=pass score=123.456 metric=validated_workflows_per_second direction=maximize
```

Use `status=fail` and a stable reason token when any gate fails. The score is comparable only when status is `pass`.

Choose a direct production outcome such as validated workflows per second, p95 latency, or bytes transferred. Prefer median score across fixed equal-duration trials. If scenarios require aggregation, freeze the formula, such as a geometric mean of normalized throughput.

Correctness must never be traded for speed. Define a hard-gate matrix covering applicable behavior:

| Gate | Required evidence |
| --- | --- |
| Success | Exact result values, counts, ordering where promised, and side effects |
| Failure | Expected exception/result and final persisted state after each failure point |
| Cancellation | Prompt cancellation, defined state, and reusable resources |
| Transaction | Commit/rollback and partial-failure semantics match the contract |
| Read-side mutation | Refreshes, counters, timestamps, and other read effects match |
| Concurrency | Controlled interleavings preserve visibility and conflict behavior |
| Lifecycle | Connections, streams, locks, memory, and temporary state are released |
| Repeatability | Fixed seed and stable outcome across confirmation trials |

## Baseline, noise, and drift

Run setup and gates before timing. Warm up equally, then run repeated equal-duration trials. Record the median and dispersion. Define the win threshold from observed variance before experiments; a score inside the noise band is not a win.

Use timing terms consistently: scenario duration is the measurement time for one scenario; trial duration covers its full fixed scenario set; confirmation trial count is the number of repeated trials for a possible win; total experiment budget bounds all setup, trials, and confirmation for one hypothesis; hard process timeout terminates a hung evaluation. Freeze all five before the baseline.

Rerun the baseline periodically and whenever external state may have changed. Pin service images, packages, runtime settings, schema, and relevant environment variables. If evaluator or workload changes, mark earlier rows incomparable and establish a new baseline series.

Inspect the candidate for evaluator detection, hard-coded fixtures, skipped work, cached cross-trial state, weakened durability, or other score gaming. A faster result that violates the semantic slice fails.

Use scout results only to decide whether promotion trials are warranted. Canonical promotion and independent reproduction use the same frozen evaluator with larger predeclared paired-trial counts. Do not repeatedly sample until significance or change tier sizes after seeing a candidate result.

Freeze required scenario identities in the protected manifest. For the selected tier, require the exact pair-index set `1..N` and complete Cartesian scenario-by-pair coverage. Reject missing or extra pair indices, missing scenarios in any pair, duplicate scenario/pair rows, and scenario-row inflation; never treat raw result-row count as paired-trial count.

## Experiment and Git protocol

Use one hypothesis per experiment commit. Before running:

1. confirm candidate content matches the last confirmed winning experiment, even if the current recovery/revert `HEAD` is a later commit;
2. confirm only declared mutable paths changed;
3. commit the candidate change;
4. run with the fixed budget and timeout;
5. append the result and preserve `run.log` for diagnosis.

Keep a confirmed win as the new best experiment commit. For a rejection, create a narrow revert commit in the dedicated worktree or restore only the declared candidate content with an explicit patch and commit it. The next experiment may descend from that recovery `HEAD`, but its candidate content must match the best state first; `parent_best_commit` in the ledger identifies the last confirmed winner, not necessarily the experiment commit's direct Git parent. Never use `git reset --hard`, broad checkout restoration, or destructive cleanup. Never use the assistant identity in attribution; retain the repository-configured user identity.

Record every attempt using the supplied TSV columns. Keep `results.tsv` intentionally untracked during search and preserve or copy it into final handoff evidence. If project governance requires a committed audit ledger, decide its path and policy before the baseline and freeze that choice. Notes should identify failure tokens, trial scores, or the reason a noisy result was rejected.

## Independent reproduction and handoff

Reproduce the winner in a fresh worktree from the recorded base commit, applying only the retained experiment history. Recreate pinned dependencies, rerun all gates, baseline, and confirmation trials, and compare the candidate diff to protected paths.

The handoff must include:

- target slice and semantic exclusions;
- base, baseline, and winning commit IDs;
- environment and dependency pins;
- scalar metric, direction, budget, timeout, noise threshold, and trial scores;
- hard-gate results and known risks;
- experiment ledger and exact candidate diff;
- a proposed production integration plan.

Stop at this brief. Do not copy the winner into production without explicit authorization.

When a bounded swarm or LLM readiness review is requested, also apply `swarm-readiness-contract.md`. The deterministic evaluator and gate remain the only research-decision authority. Readiness is advisory and cannot change the winner, score, ledger decision, or gate exit in this version.

## Concise PostgreSQL example

For a batch write followed immediately by a batch read, use the current production-shaped `SetMultiple` then `GetMultiple` workflow as the relevant two-trip baseline. Individual operations may be diagnostic context but must not inflate the claimed improvement. Execute against a live pinned PostgreSQL instance and instrument protocol behavior; do not score constants labeled “two trips” and “one trip.”

Gate exact payloads, expiration behavior, and any read-side mutation such as sliding-expiration refresh. Also inject read and write failures: combining write and read in one batch may roll back writes when the read fails, while the two-operation baseline may already have committed them. Exercise concurrency and controlled interleavings because collapsing two calls removes the interval in which other operations may observe or change state. Treat transaction, read-side mutation, or interleaving differences as semantic failures unless the frozen contract explicitly requires the new behavior.
