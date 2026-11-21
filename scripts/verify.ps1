. (Join-Path $PSScriptRoot 'common.ps1')
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$timer = [Diagnostics.Stopwatch]::StartNew()
$environmentBefore = @{}
foreach ($name in @('UseSharedCompilation', 'MSBUILDDISABLENODEREUSE', 'DOTNET_CLI_USE_MSBUILD_SERVER', 'TEMP', 'TMP', 'HNMEMO_TEST_OUTPUT')) {
    $environmentBefore[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
$logRoot = [IO.Path]::GetFullPath((Join-Path $MemoRoot 'artifacts/verify'))
$runRoot = [IO.Path]::GetFullPath((Join-Path $logRoot ('run-' + [Guid]::NewGuid().ToString('N'))))
if ([IO.Path]::GetDirectoryName($runRoot) -ne $logRoot) { throw 'Invalid verification work directory.' }
$passed = $false
Push-Location $MemoRoot
try {
    $env:UseSharedCompilation = 'false'
    $env:MSBUILDDISABLENODEREUSE = '1'
    $env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
    $temporary = Join-Path $runRoot 'temp'
    $results = Join-Path $runRoot 'results'
    New-Item -ItemType Directory -Force $temporary | Out-Null
    $env:TEMP = $temporary
    $env:TMP = $temporary
    $env:HNMEMO_TEST_OUTPUT = $runRoot
    & node --test (Join-Path $PSScriptRoot 'analyze-wrist-log.test.mjs')
    if ($LASTEXITCODE -ne 0) { throw 'Wrist-log self-tests failed.' }
    foreach ($folder in @('src', 'tests')) {
        $formatRoot = Join-Path $MemoRoot $folder
        $excluded = @(Get-ChildItem $folder -Recurse | Where-Object {
            ($_.PSIsContainer -and $_.Name -in @('bin', 'obj', 'vendor', 'artifacts', 'generated')) -or
            (-not $_.PSIsContainer -and $_.Name -match '\.(g|generated)\.cs$')
        } | ForEach-Object { $_.FullName.Substring($formatRoot.Length + 1) })
        Invoke-MemoDotnet format whitespace $folder --folder --exclude $excluded --verify-no-changes --verbosity minimal
    }
    Invoke-MemoDotnet restore HnMemo.slnx --locked-mode --verbosity minimal -warnaserror '-warnnotaserror:NU1900'
    # Recompile references too: an incremental build could conceal old warnings.
    Invoke-MemoDotnet build HnMemo.slnx --no-restore --configuration Release --no-incremental --verbosity minimal -warnaserror '-warnnotaserror:NU1900'
    $log = Join-Path $logRoot 'tests.log'
    $solution = Join-Path $MemoRoot 'HnMemo.slnx'
    Push-Location $runRoot
    $ErrorActionPreference = 'Continue'
    try {
        & $MemoDotnet test $solution --no-build --configuration Release --filter 'TestCategory!=Manual' --results-directory $results --logger trx 2>&1 |
            Tee-Object -FilePath $log | Write-Host
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = 'Stop'; Pop-Location }
    if ($code -ne 0) { throw "dotnet test failed (exit $code). See $log" }

    $minimum = @{ 'HandwriteMemo.Core.Tests' = 74; 'HandwriteMemo.Storage.Tests' = 32; 'HandwriteMemo.App.Tests' = 61 }
    $recorded = @{}
    foreach ($file in Get-ChildItem -LiteralPath $results -Filter '*.trx') {
        [xml]$report = Get-Content -Raw -LiteralPath $file.FullName
        $run = $report.TestRun
        $assemblies = @($run.TestDefinitions.UnitTest | ForEach-Object {
            [IO.Path]::GetFileNameWithoutExtension($_.TestMethod.codeBase)
        } | Sort-Object -Unique)
        if ($assemblies.Count -ne 1 -or !$minimum.ContainsKey($assemblies[0]) -or $recorded.ContainsKey($assemblies[0])) {
            throw "Unexpected test assembly(s) in $($file.FullName): $assemblies"
        }
        $name = $assemblies[0]
        $counters = $run.ResultSummary.Counters
        $total = [int]$counters.total
        $testResults = @($run.Results.UnitTestResult)
        if ($total -lt $minimum[$name] -or [int]$counters.executed -ne $total -or
            [int]$counters.passed -ne $total -or $testResults.Count -ne $total -or
            @($testResults | Where-Object { $_.outcome -ne 'Passed' }).Count -ne 0) {
            throw "Expected all $name tests to pass (minimum $($minimum[$name])). See $($file.FullName)."
        }
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $logRoot "$name.trx") -Force
        $recorded[$name] = $total
        Write-Host "$name : $total passed, 0 failed, 0 skipped."
    }
    if ($recorded.Count -ne $minimum.Count) { throw "Missing TRX suites. See $results" }

    $passed = $true
    Write-Host 'VERIFY PASSED: script tests, whitespace, locked restore, solution build, Core/Storage/App tests.'
} finally {
    Pop-Location
    foreach ($name in $environmentBefore.Keys) {
        [Environment]::SetEnvironmentVariable($name, $environmentBefore[$name], 'Process')
    }
    if ($passed) {
        Remove-Item -LiteralPath $runRoot -Recurse -Force
    } else {
        Write-Host "Verification diagnostics: $runRoot"
    }
    Write-Host ('Verification elapsed: {0:F2} seconds' -f $timer.Elapsed.TotalSeconds)
}
