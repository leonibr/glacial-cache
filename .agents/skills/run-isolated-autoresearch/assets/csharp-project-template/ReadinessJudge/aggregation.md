# Frozen aggregation contract

Run exactly three blinded, no-tool primary judges with the pinned configuration. Validate each
response against `verdict.schema.json`. Use an adjudicator only for unresolved disagreement, never
for a missing or failed primary response.

Compute the median 0-4 rating for each dimension. Compute the weighted percentage as the sum of
`median / 4 * weight`. A critical blocker exists only when at least two primary judges return the
same taxonomy token. Assign `ready` at 80 or above when every median is at least 2 and there is no
consensus blocker; assign `conditional` from 65 through 79.999 under the same constraints; otherwise
assign `not_ready`. Assign `inconclusive` when evidence is missing, any primary result is unavailable
or invalid, or disagreement remains after the one permitted adjudication.

Store `researchDecision` and `readinessStatus` as separate fields. Readiness never changes the
deterministic winner, score, or gate exit code in this template. Do not enable readiness as a gate
without a new frozen series and calibration demonstrating 100% critical-blocker recall, less than
10% false-positive blockers, and weighted kappa of at least 0.70.

The Evaluation Authority must build the evidence packet outside worker-writable storage and validate
it against `evidence.schema.json`. This template does not implement or claim a source-code sanitizer.
The builder must exclude candidate prose, comments, commit messages, swarm chat, mutable instructions,
lessons, and arbitrary raw logs; redact string literals; JSON-escape remaining code-derived text; and
mark it as untrusted. A schema declaration is not proof that sanitization occurred, so retain the
source hash and builder provenance in authority-owned evidence.
