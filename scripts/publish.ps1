param(
    [string]$OutputDirectory = 'dist\HnMemo',
    [bool]$SelfContained = $true
)

. (Join-Path $PSScriptRoot 'common.ps1')
Push-Location $MemoRoot
try {
    $contained = $SelfContained.ToString().ToLowerInvariant()
    Invoke-MemoDotnet restore $MemoProject --locked-mode --runtime win-x64 "-p:SelfContained=$contained" "-p:EnableRuntimePackDownload=$contained" -p:NuGetLockFilePath=packages.win-x64.lock.json --verbosity minimal
    # Omit debug symbols and map source paths so distributed binaries do not contain a developer's home path.
    Invoke-MemoDotnet publish $MemoProject --no-restore --configuration Release --runtime win-x64 --self-contained $contained --output $OutputDirectory --verbosity minimal "-p:PathMap=$MemoRoot=/_/HnMemo" -p:DebugType=None -p:DebugSymbols=false -p:PublishSingleFile=false -p:PublishTrimmed=false
} finally { Pop-Location }
