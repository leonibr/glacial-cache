---
name: run-isolated-autoresearch
description: Isolate a production-relevant repository slice and build or run a guarded Karpathy-style autoresearch loop with a frozen evaluator, one scored objective, autonomous hypothesis experiments, optional bounded research swarms, and advisory LLM production-readiness review. Use when asked to create, rearchitect, or run autoresearch, autonomous optimization, or scored improvement loops. Do not use for ordinary benchmarks, one-off fixes, parameter or workload sweeps, or production integration.
---

# Run Isolated Autoresearch

Turn one production behavior into a reproducible search loop without allowing workers or judges to
redefine success.

## Load the contracts

Read [references/lab-contract.md](references/lab-contract.md) completely before changing a repository.
For multi-agent search or readiness judging, also read
[references/swarm-readiness-contract.md](references/swarm-readiness-contract.md). For the bundled C#
template, read [references/template-tokens.md](references/template-tokens.md).

## Qualify the slice

1. Inspect repository instructions, worktree state, production behavior, callers, tests, benchmarks,
   and existing labs. Stop for an explicit provenance choice if dirty changes overlap the slice.
2. State the user value, semantic boundary, relevant production baseline, and multiple plausible
   implementation hypotheses.
3. Apply the eligibility gate. Reject predetermined results, modeled primary metrics, renamed workload
   sweeps, single known fixes, or claims that cannot be observed affordably with a frozen evaluator.

## Isolate and freeze

1. Record an explicit base commit and create a dedicated worktree/branch without disturbing user work.
2. For .NET, copy [assets/csharp-project-template](assets/csharp-project-template), rename
   `results.tsv.template` to `results.tsv`, and replace every documented token. Preserve existing labs.
3. Replace the example `Contract` and fail-closed `Evaluator` with the narrow production-shaped API,
   protected baseline, oracle, paired randomized A/B trials, instrumentation, and score. Do not set
   the manifest `contractConfigured` flag true until this is complete.
4. Freeze the workload, seed, durations, budgets, timeout, scalar metric, noise rule, semantic gates,
   external image/package pins, provenance, tier sizes, and protected paths before taking the baseline.
5. Verify the template using `scripts/verify-csharp-template.ps1` before adapting it, then use normal
   `dotnet run` from the copied lab root to build and test the adapted evaluator and gate again.

Only `Candidate/Candidate.cs` is worker-editable by default. The coordinator schedules leases and
budgets; workers propose candidate commits; the serialized Evaluation Authority alone owns canonical
evaluation, holdouts, gate decisions, and retained-best state.
Canonical reports, manifests, changed-path lists, and authority contexts must live in storage workers
cannot modify. The template validates binding and content; it does not claim to sandbox its own files.

## Establish and search

Run setup, semantic gates, and repeated baseline trials. Record median and dispersion. Any evaluator
change invalidates prior comparisons and starts a new series.

For each leased experiment: begin from retained-best candidate content; state one falsifiable
hypothesis; change only the candidate file; commit using repository attribution; verify changed paths;
run within the frozen budget; gate the machine-readable artifact; append the ledger; confirm possible
wins; keep credible improvements; recoverably restore rejected content. Never use a hard reset or
broad checkout restoration. Treat scenario sweeps as coverage inside one experiment.

Use scout, promotion, and independent-reproduction tiers. A win requires all semantic gates, a paired
improvement confidence interval excluding zero, and no scenario regression over 5% unless a different
threshold was frozen before the baseline.

## Judge readiness and hand off

Run readiness review only after deterministic promotion. Keep it advisory and separate from
`researchDecision`. Use exactly three blinded, no-tool judges and at most one adjudicator with pinned
model, prompt, rubric, schemas, and sanitized evidence. Readiness must never alter the winner in this
version.

Have an independent verifier reproduce the winner in a fresh worktree. Hand off provenance, exact
candidate diff, environment pins, trials, gates, variance, ledger, risks, and readiness advisory. Stop
before production integration; require a separate explicit request and normal review.
