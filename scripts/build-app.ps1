. (Join-Path $PSScriptRoot 'common.ps1')

$dist = [IO.Path]::GetFullPath((Join-Path $MemoRoot 'dist'))
$output = Join-Path $dist 'HnMemo'
$id = [Guid]::NewGuid().ToString('N')
$stage = Join-Path $dist "HnMemo-stage-$id"
$previous = Join-Path $dist "HnMemo-previous-$id"
New-Item -ItemType Directory -Path $stage -Force | Out-Null

Push-Location $MemoRoot
try {
    Invoke-MemoDotnet publish $MemoProject --configuration Release --output $stage -p:RestoreLockedMode=true --verbosity minimal
    if (Test-Path -LiteralPath $output) {
        [IO.Directory]::Move($output, $previous)
    }
    try {
        [IO.Directory]::Move($stage, $output)
    } catch {
        if (Test-Path -LiteralPath $previous) {
            [IO.Directory]::Move($previous, $output)
        }
        throw
    }
    if (Test-Path -LiteralPath $previous) {
        [IO.Directory]::Delete($previous, $true)
    }
    Write-Host 'HnMemo published to dist/HnMemo.'
} finally {
    Pop-Location
    if (Test-Path -LiteralPath $stage) {
        [IO.Directory]::Delete($stage, $true)
    }
}
