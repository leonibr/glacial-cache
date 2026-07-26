[CmdletBinding()]
param(
    [string]$TemplatePath = (Join-Path (Split-Path $PSScriptRoot -Parent) 'assets/csharp-project-template')
)

$ErrorActionPreference = 'Stop'
$template = (Resolve-Path $TemplatePath).Path
$skillRoot = Split-Path $PSScriptRoot -Parent
$scratch = Join-Path ([System.IO.Path]::GetTempPath()) ("autoresearch-template-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch | Out-Null

function Assert-ExitCode([int]$Expected, [string]$Name) {
    if ($LASTEXITCODE -ne $Expected) { throw "$Name returned $LASTEXITCODE instead of $Expected." }
}

try {
    Get-ChildItem -Path $template -Recurse -Filter '*.json' | ForEach-Object {
        Get-Content -Raw $_.FullName | ConvertFrom-Json | Out-Null
    }

    $projects = @(
        (Join-Path $template 'Evaluator/Evaluator.csproj'),
        (Join-Path $template 'Gate/Gate.csproj')
    )
    foreach ($project in $projects) {
        & dotnet build $project -c Release --artifacts-path (Join-Path $scratch 'build') --nologo
        Assert-ExitCode 0 "Build $project"
    }

    $gateDll = Get-ChildItem -Path (Join-Path $scratch 'build') -Recurse -Filter 'Gate.dll' |
        Where-Object { $_.FullName -match '[\\/]release[\\/]' } |
        Select-Object -First 1 -ExpandProperty FullName
    $evaluatorDll = Get-ChildItem -Path (Join-Path $scratch 'build') -Recurse -Filter 'Evaluator.dll' |
        Where-Object { $_.FullName -match '[\\/]release[\\/]' } |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $gateDll -or -not $evaluatorDll) { throw 'Expected evaluator and gate build outputs were not produced.' }

    $evaluatorOutput = & dotnet $evaluatorDll
    Assert-ExitCode 3 'Unconfigured evaluator'
    if ($evaluatorOutput -notmatch 'reason=replace_example_evaluator') {
        throw 'Unconfigured evaluator did not fail closed with the expected reason.'
    }

    $fixtures = Join-Path $template 'Tests/fixtures'
    $keep = Join-Path $fixtures 'evaluation.keep.json'
    $reject = Join-Path $fixtures 'evaluation.reject.json'
    $paths = Join-Path $fixtures 'changed-paths.valid.txt'
    $manifest = Join-Path $fixtures 'gate-manifest.json'
    $authority = Join-Path $fixtures 'authority-context.json'

    & dotnet $gateDll $keep $paths $manifest $authority
    Assert-ExitCode 0 'Keep fixture'
    & dotnet $gateDll $reject $paths $manifest $authority
    Assert-ExitCode 1 'Reject fixture'
    & dotnet $gateDll $keep (Join-Path $fixtures 'changed-paths.protected.txt') $manifest $authority
    Assert-ExitCode 5 'Protected-path fixture'
    & dotnet $gateDll $keep (Join-Path $fixtures 'changed-paths.case-variant.txt') $manifest $authority
    Assert-ExitCode 5 'Case-variant path fixture'
    & dotnet $gateDll $keep (Join-Path $fixtures 'missing.txt') $manifest $authority
    Assert-ExitCode 4 'Missing input fixture'

    $behavior = Get-Content -Raw $keep | ConvertFrom-Json
    $behavior.status = 'fail'
    $behavior.reason = 'semantic_gate_failed'
    $behavior.gates[0].passed = $false
    $behaviorPath = Join-Path $scratch 'evaluation.behavior.json'
    $behavior | ConvertTo-Json -Depth 20 | Set-Content -Encoding utf8 $behaviorPath
    & dotnet $gateDll $behaviorPath $paths $manifest $authority
    Assert-ExitCode 2 'Behavioral failure fixture'

    $forged = Get-Content -Raw $keep | ConvertFrom-Json
    $forged.metric = 'forged_metric'
    $forgedPath = Join-Path $scratch 'evaluation.forged.json'
    $forged | ConvertTo-Json -Depth 20 | Set-Content -Encoding utf8 $forgedPath
    & dotnet $gateDll $forgedPath $paths $manifest $authority
    Assert-ExitCode 3 'Forged metric fixture'

    $forgedCandidate = Get-Content -Raw $keep | ConvertFrom-Json
    $forgedCandidate.candidateCommit = 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'
    $forgedCandidatePath = Join-Path $scratch 'evaluation.forged-candidate.json'
    $forgedCandidate | ConvertTo-Json -Depth 20 | Set-Content -Encoding utf8 $forgedCandidatePath
    & dotnet $gateDll $forgedCandidatePath $paths $manifest $authority
    Assert-ExitCode 3 'Forged candidate fixture'

    $inverted = Get-Content -Raw $keep | ConvertFrom-Json
    $inverted.confidenceIntervalLow = 10.0
    $inverted.confidenceIntervalHigh = 1.0
    $invertedPath = Join-Path $scratch 'evaluation.inverted-ci.json'
    $inverted | ConvertTo-Json -Depth 20 | Set-Content -Encoding utf8 $invertedPath
    & dotnet $gateDll $invertedPath $paths $manifest $authority
    Assert-ExitCode 3 'Inverted confidence interval fixture'

    $skippedPair = Get-Content -Raw $keep | ConvertFrom-Json
    $skippedPair.trials[2].pairIndex = 4
    $skippedPairPath = Join-Path $scratch 'evaluation.skipped-pair.json'
    $skippedPair | ConvertTo-Json -Depth 20 | Set-Content -Encoding utf8 $skippedPairPath
    & dotnet $gateDll $skippedPairPath $paths $manifest $authority
    Assert-ExitCode 3 'Skipped pair index fixture'

    $extraPair = Get-Content -Raw $keep | ConvertFrom-Json
    $extraPair.trials = @($extraPair.trials) + [pscustomobject]@{
        scenario = 'representative'; pairIndex = 4; baseline = 100.0; candidate = 105.0
    }
    $extraPairPath = Join-Path $scratch 'evaluation.extra-pair.json'
    $extraPair | ConvertTo-Json -Depth 20 | Set-Content -Encoding utf8 $extraPairPath
    & dotnet $gateDll $extraPairPath $paths $manifest $authority
    Assert-ExitCode 3 'Extra pair index fixture'

    $multiManifest = Get-Content -Raw $manifest | ConvertFrom-Json
    $multiManifest.requiredScenarios = @('representative', 'secondary')
    $multiManifestPath = Join-Path $scratch 'manifest.multi-scenario.json'
    $multiManifest | ConvertTo-Json -Depth 20 | Set-Content -Encoding utf8 $multiManifestPath
    $multi = Get-Content -Raw $keep | ConvertFrom-Json
    $multi.trials = @($multi.trials) + @(
        [pscustomobject]@{ scenario = 'secondary'; pairIndex = 1; baseline = 90.0; candidate = 95.0 },
        [pscustomobject]@{ scenario = 'secondary'; pairIndex = 2; baseline = 91.0; candidate = 96.0 },
        [pscustomobject]@{ scenario = 'secondary'; pairIndex = 3; baseline = 89.0; candidate = 94.0 }
    )
    $multiPath = Join-Path $scratch 'evaluation.multi-scenario.json'
    $multi | ConvertTo-Json -Depth 20 | Set-Content -Encoding utf8 $multiPath
    & dotnet $gateDll $multiPath $paths $multiManifestPath $authority
    Assert-ExitCode 0 'Complete multi-scenario matrix fixture'

    $missingScenario = Get-Content -Raw $multiPath | ConvertFrom-Json
    $missingScenario.trials = @($missingScenario.trials | Where-Object {
        -not ($_.scenario -eq 'secondary' -and $_.pairIndex -eq 3)
    })
    $missingScenarioPath = Join-Path $scratch 'evaluation.missing-scenario.json'
    $missingScenario | ConvertTo-Json -Depth 20 | Set-Content -Encoding utf8 $missingScenarioPath
    & dotnet $gateDll $missingScenarioPath $paths $multiManifestPath $authority
    Assert-ExitCode 3 'Missing scenario coverage fixture'

    $threeScenarioManifest = Get-Content -Raw $manifest | ConvertFrom-Json
    $threeScenarioManifest.requiredScenarios = @('scenario_a', 'scenario_b', 'scenario_c')
    $threeScenarioManifestPath = Join-Path $scratch 'manifest.three-scenario.json'
    $threeScenarioManifest | ConvertTo-Json -Depth 20 | Set-Content -Encoding utf8 $threeScenarioManifestPath
    $threeRowsOnePair = Get-Content -Raw $keep | ConvertFrom-Json
    $threeRowsOnePair.trials = @(
        [pscustomobject]@{ scenario = 'scenario_a'; pairIndex = 1; baseline = 100.0; candidate = 105.0 },
        [pscustomobject]@{ scenario = 'scenario_b'; pairIndex = 1; baseline = 100.0; candidate = 105.0 },
        [pscustomobject]@{ scenario = 'scenario_c'; pairIndex = 1; baseline = 100.0; candidate = 105.0 }
    )
    $threeRowsOnePairPath = Join-Path $scratch 'evaluation.three-rows-one-pair.json'
    $threeRowsOnePair | ConvertTo-Json -Depth 20 | Set-Content -Encoding utf8 $threeRowsOnePairPath
    & dotnet $gateDll $threeRowsOnePairPath $paths $threeScenarioManifestPath $authority
    Assert-ExitCode 3 'Three scenario rows using one pair fixture'

    $promotionManifest = Get-Content -Raw $manifest | ConvertFrom-Json
    $promotionManifest.requiredScenarios = 1..7 | ForEach-Object { "scenario_$_" }
    $promotionManifestPath = Join-Path $scratch 'manifest.promotion-seven-scenario.json'
    $promotionManifest | ConvertTo-Json -Depth 20 | Set-Content -Encoding utf8 $promotionManifestPath
    $promotionAuthority = Get-Content -Raw $authority | ConvertFrom-Json
    $promotionAuthority.tier = 'promotion'
    $promotionAuthorityPath = Join-Path $scratch 'authority.promotion.json'
    $promotionAuthority | ConvertTo-Json -Depth 20 | Set-Content -Encoding utf8 $promotionAuthorityPath
    $promotionRowsOnePair = Get-Content -Raw $keep | ConvertFrom-Json
    $promotionRowsOnePair.tier = 'promotion'
    $promotionRowsOnePair.trials = 1..7 | ForEach-Object {
        [pscustomobject]@{ scenario = "scenario_$_"; pairIndex = 1; baseline = 100.0; candidate = 105.0 }
    }
    $promotionRowsOnePairPath = Join-Path $scratch 'evaluation.promotion-rows-one-pair.json'
    $promotionRowsOnePair | ConvertTo-Json -Depth 20 | Set-Content -Encoding utf8 $promotionRowsOnePairPath
    & dotnet $gateDll $promotionRowsOnePairPath $paths $promotionManifestPath $promotionAuthorityPath
    Assert-ExitCode 3 'Promotion scenario-row inflation fixture'

    $unconfiguredManifest = Get-Content -Raw $manifest | ConvertFrom-Json
    $unconfiguredManifest.targetSlice = 'unconfigured_for_scaffold'
    $unconfiguredManifestPath = Join-Path $scratch 'manifest.unconfigured.json'
    $unconfiguredManifest | ConvertTo-Json -Depth 20 | Set-Content -Encoding utf8 $unconfiguredManifestPath
    & dotnet $gateDll $keep $paths $unconfiguredManifestPath $authority
    Assert-ExitCode 3 'Enabled unconfigured manifest fixture'

    & git -C $template check-ignore --no-index --quiet 'Environment/.secrets/probe.txt'
    Assert-ExitCode 0 'Environment secret ignore rule'

    $evidenceSchema = Get-Content -Raw (Join-Path $template 'ReadinessJudge/evidence.schema.json') | ConvertFrom-Json
    if ($evidenceSchema.additionalProperties -ne $false -or
        $evidenceSchema.properties.semanticGateSummary.minItems -lt 1 -or
        $evidenceSchema.properties.semanticGateSummary.items.additionalProperties -ne $false -or
        $evidenceSchema.properties.performanceSummary.additionalProperties -ne $false -or
        $evidenceSchema.properties.environment.additionalProperties -ne $false -or
        $evidenceSchema.properties.untrustedCandidateDiff.additionalProperties -ne $false) {
        throw 'Readiness evidence schema is not closed and substantive.'
    }
    $diffRequired = @($evidenceSchema.properties.untrustedCandidateDiff.required)
    foreach ($field in @('sourceSha256', 'candidateControlledTextRemoved', 'commentsRemoved', 'stringLiteralsRedacted')) {
        if ($field -notin $diffRequired) { throw "Readiness evidence schema does not require $field." }
    }

    $taxonomy = @(Get-Content -Raw (Join-Path $template 'ReadinessJudge/blocker-taxonomy.json') | ConvertFrom-Json | Select-Object -ExpandProperty critical)
    $verdictSchema = Get-Content -Raw (Join-Path $template 'ReadinessJudge/verdict.schema.json') | ConvertFrom-Json
    $verdictBlockers = @($verdictSchema.properties.criticalBlockers.items.enum)
    if (Compare-Object ($taxonomy | Sort-Object) ($verdictBlockers | Sort-Object)) {
        throw 'Verdict blocker enum does not match the frozen taxonomy.'
    }

    $tokenPattern = '\{\{([A-Z0-9_]+)\}\}'
    $actualTokens = Get-ChildItem -Path $template -Recurse -File | ForEach-Object {
        [regex]::Matches((Get-Content -Raw $_.FullName), $tokenPattern) | ForEach-Object { $_.Groups[1].Value }
    } | Sort-Object -Unique
    $tokenDocumentation = Get-Content -Raw (Join-Path $skillRoot 'references/template-tokens.md')
    foreach ($token in $actualTokens) {
        if ($tokenDocumentation -notmatch [regex]::Escape("``$token``")) { throw "Undocumented template token: $token" }
    }

    $global:LASTEXITCODE = 0
    Write-Output 'C# autoresearch template verification passed.'
}
finally {
    if (Test-Path -LiteralPath $scratch) {
        Remove-Item -LiteralPath $scratch -Recurse -Force
    }
}
