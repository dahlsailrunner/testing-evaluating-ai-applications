param(
    [switch]$ShowReports
)

Remove-Item -Recurse -Force -ErrorAction SilentlyContinue TestResults, coveragereport, "tests/CarvedRock.AppTests/bin/Debug/Net10.0/playwright-artifacts"

dotnet test --coverage --coverage-output-format cobertura --coverage-settings testconfig.json

reportgenerator -reports:TestResults/*.cobertura.xml -targetdir:coveragereport -reporttypes:"Html;TextSummary;"

if ($ShowReports) {
    Invoke-Item ./coveragereport/index.html
    Invoke-Item ./TestResults/*.html
}