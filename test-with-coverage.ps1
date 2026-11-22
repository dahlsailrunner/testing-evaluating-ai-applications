param(
    [switch]$ShowReports,
    [switch]$IncludeEvals   # also run the (slow, paid, LLM-judged) eval project and build its report
)

Remove-Item -Recurse -Force -ErrorAction SilentlyContinue TestResults, coveragereport, "tests/CarvedRock.AppTests/bin/Debug/net11.0/playwright-artifacts"

# TUnit report aggregation (https://tunit.dev/docs/guides/report-aggregation):
# outside GitHub Actions there is no shared directory by default, so point every
# test process at one. Each process drops its JSON sidecar there and regenerates
# merged-report.html, so the last one to finish leaves a report covering all projects.
$aggregateDir = Join-Path $PSScriptRoot "TestResults/aggregate"
$env:TUNIT_AGGREGATE_DIR = $aggregateDir

# The eval tests are slow, cost money (LLM judge calls) and aren't deterministic, so a normal run leaves
# them out. They're tagged [Category("Evals")]; this filter excludes that tag from every project
# (dotnet test --project can't take several projects, so a filter is how a single command skips one).
$filter = @()
if (-not $IncludeEvals) {
    $filter = @("--treenode-filter", "/*/*/*/*[Category!=Evals]")
}

dotnet test @filter --coverage --coverage-output-format cobertura --coverage-settings testconfig.json

reportgenerator -reports:TestResults/*.cobertura.xml -targetdir:coveragereport -reporttypes:"Html;TextSummary;"

if ($IncludeEvals) {
    # The evals already ran above, so only build the report (and open it with -ShowReports).
    # EvalResults/ is not cleaned at the top of this script: eval history is kept between runs.
    & (Join-Path $PSScriptRoot "eval-report.ps1") -Show:$ShowReports
}

if ($ShowReports) {
    Invoke-Item ./coveragereport/index.html

    $mergedReport = Join-Path $aggregateDir "merged-report.html"
    if (Test-Path $mergedReport) {
        Invoke-Item $mergedReport
    }
    else {
        Write-Warning "Aggregated TUnit report not found at $mergedReport"
    }
}