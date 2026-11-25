param(
    [string]$OutputRoot = (Join-Path $PSScriptRoot '../artifacts/foundation'),
    [string]$Before = ''
)
. (Join-Path $PSScriptRoot 'common.ps1')
$output = [System.IO.Path]::GetFullPath($OutputRoot)
$project = Join-Path $MemoRoot 'tests/HandwriteMemo.App.Tests'
$after = Join-Path $output 'after'
$baseline = if ($Before) { (Resolve-Path -LiteralPath $Before).Path } else { '' }
if ($baseline -and ($baseline.TrimEnd('\', '/') -eq $after.TrimEnd('\', '/'))) {
    throw 'Before must be a separate, previously captured directory.'
}
$previousOutput = $env:HNVRMEMO_FOUNDATION_OUTPUT
$previousBefore = $env:HNVRMEMO_FOUNDATION_BEFORE
Push-Location $MemoRoot
try {
    $env:HNVRMEMO_FOUNDATION_OUTPUT = $output
    $env:HNVRMEMO_FOUNDATION_BEFORE = $baseline
    Invoke-MemoDotnet test $project --configuration Release --filter 'TestCategory=Manual&FullyQualifiedName~FoundationCaptureTests.CaptureAndCompare'
} finally {
    $env:HNVRMEMO_FOUNDATION_OUTPUT = $previousOutput
    $env:HNVRMEMO_FOUNDATION_BEFORE = $previousBefore
    Pop-Location
}
