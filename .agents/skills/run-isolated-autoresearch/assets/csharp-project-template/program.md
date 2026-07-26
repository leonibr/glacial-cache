# {{TARGET_SLICE}} isolated autoresearch

## Frozen research contract

- Base commit: `{{BASE_COMMIT}}`
- Baseline provenance: `{{BASELINE_PROVENANCE}}`
- User-valued behavior: {{TARGET_BEHAVIOR}}
- Required semantics: {{REQUIRED_SEMANTICS}}
- Exclusions: {{OUT_OF_SCOPE}}
- Only worker-editable path: `Candidate/Candidate.cs`
- Primary score: {{SCORE_DEFINITION}} (`{{SCORE_DIRECTION}}`)
- Workload and seed: {{WORKLOAD_AND_SEED}}
- Scenario/trial duration: {{SCENARIO_DURATION}} / {{TRIAL_DURATION}}
- Confirmation trials: {{CONFIRMATION_TRIALS}}
- Experiment budget/process timeout: {{TOTAL_EXPERIMENT_BUDGET}} / {{PROCESS_TIMEOUT}}
- Noise threshold: {{NOISE_THRESHOLD}}
- Semantic gates: {{HARD_GATES}}
- External pins: {{EXTERNAL_PINS}}
- Instrumentation: {{INSTRUMENTATION_METHOD}}

Replace every documented placeholder token, replace the example contract and evaluator, and set the
single authoritative manifest `contractConfigured` flag before recording the baseline. Freeze all files
except `Candidate/Candidate.cs`. Any later protected change starts a new results series and baseline.
Use `unconfigured_for_scaffold` only while scaffolding; an enabled baseline must not retain it in any
protected expectation. Readiness-only provider/model/hash fields may retain it only when
`ReadinessJudge/judge-config.json` mode is `disabled` and judges are not invoked.

## Deterministic evaluation

Use randomized paired A/B trials against the protected production baseline. Scout with 3 pairs,
promote with 7, and independently reproduce with 11 unless the frozen manifest is changed before the
baseline. The evaluator must emit `artifacts/evaluation.json` and one parseable summary line. The
confidence interval for improvement must exclude zero and no scenario may regress by more than 5%.
List every scenario identity in the protected manifest. Each tier report must contain the exact
Cartesian matrix of required scenarios and pair indices `1..N`, with no skipped/extra indices or
duplicate scenario/pair rows.

Run the evaluator, list changed paths, then invoke the gate:

```text
dotnet run -c Release --project Evaluator/Evaluator.csproj > run.log 2>&1
git diff --name-only --relative=. <parent_best_commit> HEAD > artifacts/changed-paths.txt
dotnet run -c Release --project Gate/Gate.csproj -- artifacts/evaluation.json artifacts/changed-paths.txt lab.manifest.json artifacts/evaluation-authority-context.json
```

Gate exits: `0 keep`, `1 reject`, `2 behavioral failure`, `3 invalid/incomparable`, `4 infrastructure
failure`, `5 protected-path violation`.

Run canonical evaluation and the gate only as the Evaluation Authority. Store the manifest,
changed-path list, authority context, and evaluation report where workers cannot write them. The local
template validates content and binding but cannot create its own operating-system sandbox. The trusted
authority context must contain the leased candidate commit, environment fingerprint, and evaluation
tier; never derive those expected values from the candidate report.

The verification script uses scratch build output. After adapting a copied lab, use normal
`dotnet run` from the lab root so the evaluator writes to that lab's `artifacts/` directory.

## Bounded swarm

Use two profiling scouts and two hypothesis workers, each in an isolated worktree, branch, build
directory, database, credential set, and port range. Use one serialized Evaluation Authority. The
coordinator may issue leases and budgets but cannot edit, evaluate, access holdouts, or select a
winner. A stale lease cannot promote. Combine proposals only as a new experiment; never auto-merge.

For every experiment: start from retained-best candidate content; state one falsifiable hypothesis;
edit only `Candidate/Candidate.cs`; commit it using repository attribution; submit the commit SHA;
evaluate under the fixed budget; append `results.tsv`; retain a confirmed win or recoverably restore
the prior candidate. Never use a hard reset or broad checkout restoration.

## Advisory readiness review

After deterministic promotion only, produce a sanitized evidence packet. Exclude commit messages,
swarm conversation, candidate-authored Markdown/comments, mutable instructions, and arbitrary raw
logs. Escape code-derived text as untrusted and strip string literals. Run exactly three blinded,
no-tool judges using the pinned prompt/model/rubric/schema and at most one adjudicator. Store
`researchDecision` and `readinessStatus` separately. Readiness never changes the winner in this lab.

## Stop

Have an independent verifier reproduce the winner in a fresh worktree from the recorded base. Hand
off the exact candidate diff, provenance, trials, gates, risks, readiness advisory, ledger, and
environment pins. Do not modify production without a separate explicit request.
