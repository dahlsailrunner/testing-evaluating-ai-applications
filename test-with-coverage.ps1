param(
    [switch]$ShowReports
)

Remove-Item -Recurse -Force -ErrorAction SilentlyContinue TestResults, coveragereport, "tests/CarvedRock.AppTests/bin/Debug/Net10.0/playwright-artifacts"

# TUnit report aggregation (https://tunit.dev/docs/guides/report-aggregation):
# outside GitHub Actions there is no shared directory by default, so point every
# test process at one. Each process drops its JSON sidecar there and regenerates
# merged-report.html, so the last one to finish leaves a report covering all projects.
$aggregateDir = Join-Path $PSScriptRoot "TestResults/aggregate"
$env:TUNIT_AGGREGATE_DIR = $aggregateDir

dotnet test --coverage --coverage-output-format cobertura --coverage-settings testconfig.json

reportgenerator -reports:TestResults/*.cobertura.xml -targetdir:coveragereport -reporttypes:"Html;TextSummary;"

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