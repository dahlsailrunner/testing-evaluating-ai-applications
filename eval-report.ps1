param(
    [switch]$Run,   # run the eval tests first
    [switch]$Show   # open the report when it's built
)

# Builds a standalone HTML report from the evaluation results that tests/CarvedRock.EvalTests
# writes to EvalResults/. Every run is kept as its own "execution", so the report can compare them.
# (test-with-coverage.ps1 deletes TestResults/ on purpose; this folder is separate so history survives.)
#
#   ./eval-report.ps1            build the report from the results already on disk
#   ./eval-report.ps1 -Run -Show run the evals, build the report, open it

$resultsDir = Join-Path $PSScriptRoot "EvalResults"
$report = Join-Path $resultsDir "report.html"

if ($Run) {
    dotnet run --project (Join-Path $PSScriptRoot "tests/CarvedRock.EvalTests")

    # A failing eval is a result, not a reason to stop: the report is how you look at the failure.
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "The eval tests exited with code $LASTEXITCODE. Building the report anyway."
    }
}

if (-not (Test-Path $resultsDir)) {
    Write-Error "No results found at $resultsDir. Run the evals first: ./eval-report.ps1 -Run"
    exit 1
}

dotnet tool restore | Out-Null
dotnet aieval report --path $resultsDir --output $report

if ($Show) {
    Invoke-Item $report
}
