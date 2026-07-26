# Swarm and readiness contract

## Authority and isolation

- The coordinator may queue hypotheses, issue expiring leases, and enforce budgets. It cannot edit a
  candidate, run canonical evaluation, see holdouts, declare a winner, or merge proposals.
- Begin with two profiling scouts, two hypothesis workers, and one serialized Evaluation Authority.
- Give each worker a distinct worktree, branch, build output, database, credentials, and ports. Mount
  protected inputs read-only and restrict resources and network access to required dependencies.
- Workers may edit only `Candidate/Candidate.cs` and submit a commit SHA plus one hypothesis. Reject
  stale leases and protected-path changes. Combining proposals is a new experiment, never an auto-merge.
- The Evaluation Authority owns canonical trials, holdouts, gate execution, the ledger, and retained
  best. An independent verifier reproduces from the recorded base in a fresh worktree.

Keep canonical inputs and outputs in authority-owned storage that workers cannot modify. The template
does not sandbox itself; worktree separation alone is not an authorization boundary.

The bundled `ControlPlane` files define a contract, not a runtime. `runtimeImplemented` is false and no
SQLite support is claimed. If implementing a coordinator later, use transactional durable state (SQLite
is sufficient for one host), atomic lease acquisition, expiry, idempotent submissions, and a single
evaluation consumer. Do not coordinate concurrent workers through a shared mutable JSON file.

## Deterministic research decision

Use three frozen tiers: cheap scout, stricter promotion, and fresh independent reproduction. Randomize
the order within paired baseline/candidate A/B trials. The evaluator owns the transformation to a
positive-is-better improvement and its confidence interval. The gate returns:

| Exit | Meaning |
| --- | --- |
| 0 | keep |
| 1 | reject: insufficient or unsafe improvement |
| 2 | behavioral gate failure |
| 3 | invalid or incomparable result |
| 4 | infrastructure failure |
| 5 | protected-path violation |

Require the confidence interval to exclude zero and cap worst-scenario regression at 5% by default.
Keep these deterministic decisions independent from LLM output.

## Readiness evidence boundary

Build the packet from evaluation-owned structured artifacts. Exclude commit messages, worker/scout
conversation, candidate-authored Markdown and comments, mutable instructions, lessons, and arbitrary
raw logs. Remove or escape string literals and label any code-derived content as untrusted data. Blind
candidate identity and worker identity. Judges have no tools and cannot request more repository data.

Pin exact provider model/version, temperature, system prompt, rubric, schemas, and their hashes. Run
three independent primary judges and at most one adjudicator. Store two independent fields:

- `researchDecision = keep | reject`
- `readinessStatus = ready | conditional | not_ready | inconclusive`

## Frozen readiness calculation

Rate each dimension 0-4: Integration 20, Reliability 20, Operational 15, Security 15,
Maintainability 15, Observability 10, and Rollout 5. Take the median primary-judge rating per
dimension, then compute the weighted percentage. A critical blocker requires agreement from at least
two of three primary judges.

- `ready`: score at least 80, every dimension at least 2, no consensus blocker.
- `conditional`: score 65-79.999, every dimension at least 2, no consensus blocker.
- `not_ready`: score below 65 or a consensus blocker.
- `inconclusive`: missing evidence, invalid/failed primary response, or unresolved disagreement.

Keep readiness advisory. Enabling it as a future gate requires a new frozen results series and prior
calibration with 100% critical-blocker recall, under 10% false-positive blockers, and weighted kappa at
least 0.70. Never apply a later gate retroactively.
