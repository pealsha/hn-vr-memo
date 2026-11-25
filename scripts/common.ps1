$ErrorActionPreference = 'Stop'
$MemoRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$installedDotnet = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $installedDotnet) { throw '.NET 10 SDK is required on PATH. No tools will be installed automatically.' }
$MemoDotnet = $installedDotnet.Source
Push-Location $MemoRoot
try {
    & $MemoDotnet --version
    if ($LASTEXITCODE -ne 0) { throw 'A .NET 10 SDK compatible with global.json is required on PATH.' }
} finally { Pop-Location }
$MemoProject = Join-Path $MemoRoot 'src\HandwriteMemo.App\HandwriteMemo.App.csproj'
$MemoDll = Join-Path $MemoRoot 'src\HandwriteMemo.App\bin\Release\net10.0-windows\HnVRMemo.dll'
function Invoke-MemoDotnet {
    & $MemoDotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed (exit $LASTEXITCODE)." }
}
