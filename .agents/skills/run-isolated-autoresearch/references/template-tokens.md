# C# template tokens

Every double-braced placeholder in `assets/csharp-project-template` is intentional. During initial
scaffolding, use the exact fail-closed value `unconfigured_for_scaffold` when a value applies but is not
yet frozen. Replace it before enabling the baseline. The evaluator and gate reject an enabled manifest
whose protected expectations are unresolved, tokenized, or `unconfigured_for_scaffold`. Use
`not_applicable` only for genuinely inapplicable narrative fields and explain why in `program.md`.

| Token | Required value |
| --- | --- |
| `TARGET_SLICE` | Short behavior name |
| `BASE_COMMIT` | Full source commit SHA |
| `BASELINE_PROVENANCE` | Protected baseline origin and extraction/build details |
| `TARGET_BEHAVIOR` | User-valued behavior to optimize |
| `REQUIRED_SEMANTICS` | Observable behavior that cannot change |
| `OUT_OF_SCOPE` | Explicit exclusions |
| `SCORE_DEFINITION` | Scalar formula and units |
| `METRIC_NAME` | Stable machine metric token |
| `SCORE_DIRECTION` | `maximize` (the bundled gate's normalized improvement direction) |
| `REQUIRED_GATE_NAME` | Stable required semantic-gate token; add every required gate to the manifest array |
| `REQUIRED_SCENARIO_NAME` | Exact frozen scenario identity; add every required scenario to the manifest array |
| `WORKLOAD_AND_SEED` | Frozen scenario set and deterministic seed |
| `SCENARIO_DURATION` | Measurement time per scenario |
| `TRIAL_DURATION` | Fixed full trial duration |
| `CONFIRMATION_TRIALS` | Repeats required for a possible win |
| `TOTAL_EXPERIMENT_BUDGET` | Maximum setup plus trial wall time per hypothesis |
| `PROCESS_TIMEOUT` | Hard process termination time |
| `NOISE_THRESHOLD` | Frozen statistical/noise rejection rule |
| `HARD_GATES` | Frozen semantic gate matrix |
| `EXTERNAL_PINS` | Runtime, package, image digest, schema, and configuration pins |
| `INSTRUMENTATION_METHOD` | Direct observation method supporting the claimed metric |
| `POSTGRES_IMAGE_DIGEST` | Immutable PostgreSQL image SHA-256 digest |
| `LLM_PROVIDER` | Readiness judge provider |
| `PINNED_LLM_MODEL` | Exact readiness model name |
| `PINNED_LLM_MODEL_VERSION` | Immutable model version/snapshot |
| `JUDGE_PROMPT_SHA256` | Hash of the frozen system prompt |
| `JUDGE_RUBRIC_SHA256` | Hash of the frozen rubric |
| `EVIDENCE_SCHEMA_SHA256` | Hash of the frozen evidence schema |
| `VERDICT_SCHEMA_SHA256` | Hash of the frozen verdict schema |

If advisory readiness review is not requested, set `ReadinessJudge/judge-config.json` mode to
`disabled`, set readiness-only provider/model/hash values to `unconfigured_for_scaffold`, and do not
invoke judges. Those readiness-only values do not block deterministic research. If readiness is
enabled, replace and pin every readiness token before producing an evidence packet.
