param(
    [string]$OutputDirectory = 'artifacts\packages',
    [switch]$FrameworkDependent
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not [IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory = Join-Path $repoRoot $OutputDirectory }
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$allowedRoots = @('artifacts', 'dist') | ForEach-Object {
    [IO.Path]::GetFullPath((Join-Path $repoRoot $_)).TrimEnd('\')
}
if (-not ($allowedRoots | Where-Object {
    $outputRoot.Equals($_, [StringComparison]::OrdinalIgnoreCase) -or
    $outputRoot.StartsWith($_ + '\', [StringComparison]::OrdinalIgnoreCase)
})) { throw 'Package output must be under this repository''s artifacts/ or dist/.' }

# Every run gets a new directory. Never replace an installation or reuse a dirty publish folder.
$runRoot = Join-Path $outputRoot ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$publishRoot = Join-Path $runRoot 'publish'
$packageRoot = Join-Path $runRoot 'HnMemo'
New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
& (Join-Path $PSScriptRoot 'publish.ps1') -OutputDirectory $publishRoot -SelfContained (-not $FrameworkDependent)

# A fixed allowlist: additions to the product's dependencies must be reviewed here, too.
$runtimeFiles = @(
    'HnMemo.exe', 'HnMemo.dll', 'HandwriteMemo.Core.dll',
    'HnMemo.deps.json', 'HnMemo.runtimeconfig.json',
    'OpenVR.dll', 'openvr_api.dll', 'SharpGen.Runtime.dll', 'SharpGen.Runtime.COM.dll',
    'Vortice.Direct3D11.dll', 'Vortice.DirectX.dll', 'Vortice.DXGI.dll', 'Vortice.Mathematics.dll',
    'settings.example.json', 'LICENSE',
    'input/actions.json', 'input/app.vrmanifest', 'input/bindings_knuckles.json',
    'input/bindings_oculus_touch.json', 'input/bindings_vive_controller.json',
    'licenses/OpenVR.txt', 'licenses/FluentUI-System-Icons.txt', 'licenses/SharpGen-LICENSE.txt',
    'licenses/Vortice-LICENSE.txt', 'licenses/Vortice.Mathematics-LICENSE.txt'
)
if (-not $FrameworkDependent) {
    # The SDK's dependency manifest supplies the runtime asset list, not every file in a dirty output folder.
    $deps = Get-Content -LiteralPath (Join-Path $publishRoot 'HnMemo.deps.json') -Raw | ConvertFrom-Json
    $target = $deps.targets.($deps.runtimeTarget.name)
    $packs = @($target.PSObject.Properties | Where-Object {
        $_.Name -match '^(?:runtimepack\.)?Microsoft\.(?:NETCore|WindowsDesktop)\.App\.Runtime\.win-x64/'
    })
    if ($packs.Count -ne 2) { throw 'Both .NET and Windows Desktop runtime packs must be present in the dependency manifest.' }
    foreach ($pack in $packs) {
        foreach ($kind in @('runtime', 'native', 'resources')) {
            foreach ($asset in @($pack.Value.$kind.PSObject.Properties)) {
                if (-not $asset) { continue }
                $relative = $asset.Name
                if ($relative -match '(^|/)\.\.(/|$)' -or [IO.Path]::IsPathRooted($relative)) { throw 'Invalid runtime asset path.' }
                if ($kind -eq 'resources') {
                    $relative = $asset.Value.locale + '/' + [IO.Path]::GetFileName($relative)
                } else { $relative = [IO.Path]::GetFileName($relative) }
                $runtimeFiles += $relative
            }
        }
    }
    foreach ($required in @('coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll', 'System.Private.CoreLib.dll', 'System.Windows.Forms.dll')) {
        if ($runtimeFiles -notcontains $required) { throw "Required bundled runtime missing: $required" }
    }
    # License originals are read from the default NuGet cache populated by restore.
    $cacheLine = & dotnet nuget locals global-packages --list
    if ($LASTEXITCODE -ne 0) { throw 'Could not locate the default NuGet cache.' }
    $cacheRoot = ($cacheLine -replace '^[^:]+:\s*', '').Trim()
    foreach ($pack in $packs) {
        $id, $runtimeVersion = ($pack.Name -replace '^runtimepack\.', '') -split '/', 2
        $packRoot = Join-Path $cacheRoot ($id.ToLowerInvariant() + '/' + $runtimeVersion)
        $prefix = if ($id -like '*WindowsDesktop*') { 'WindowsDesktop' } else { 'NETRuntime' }
        $license = @('LICENSE.TXT', 'LICENSE') | ForEach-Object { Join-Path $packRoot $_ } | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
        if (-not $license) { throw "Runtime license missing: $id/$runtimeVersion" }
        New-Item -ItemType Directory -Force (Join-Path $packageRoot 'licenses') | Out-Null
        Copy-Item -LiteralPath $license -Destination (Join-Path $packageRoot "licenses/$prefix-LICENSE.txt")
        $notices = Join-Path $packRoot 'THIRD-PARTY-NOTICES.TXT'
        if (Test-Path -LiteralPath $notices -PathType Leaf) {
            Copy-Item -LiteralPath $notices -Destination (Join-Path $packageRoot "licenses/$prefix-NOTICES.txt")
        }
    }
}
$runtimeFiles = @($runtimeFiles | Sort-Object -Unique)
$userFiles = @('README.md', 'THIRD-PARTY-NOTICES.md', 'Start-HnMemo.cmd', 'Desktop-HnMemo.cmd',
    'Enable-Autostart.cmd', 'Disable-Autostart.cmd')
foreach ($relative in ($runtimeFiles + $userFiles)) {
    $sourceRoot = if ($runtimeFiles -contains $relative) { $publishRoot } else { Join-Path $repoRoot 'distribution' }
    $source = Join-Path $sourceRoot $relative
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Required distribution file missing: $relative" }
    $destination = Join-Path $packageRoot $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination
}

if ($FrameworkDependent) {
    $readme = Join-Path $packageRoot 'README.md'
    $text = [IO.File]::ReadAllText($readme)
    $claim = '**配布版は.NETを同梱する構成で、別途ランタイムを入れる必要はありません。**'
    if (-not $text.Contains($claim)) { throw 'README runtime claim was not found; check-build labeling failed.' }
    $text = $text.Replace($claim, '**この確認用ビルドは.NET未同梱です。.NET 10 Windows Desktop Runtime x64が必要です。一般配布には使わないでください。**')
    [IO.File]::WriteAllText($readme, $text, [Text.UTF8Encoding]::new($false))
    $notice = Join-Path $packageRoot 'THIRD-PARTY-NOTICES.md'
    [IO.File]::AppendAllText($notice, "`r`nThis framework-dependent check build does not bundle .NET.`r`n")
}

# Examine text and binary files, including UTF-16 strings used in .NET assemblies.
$files = @(Get-ChildItem -LiteralPath $packageRoot -Recurse -File | Sort-Object FullName)
$inventory = foreach ($file in $files) {
    $relative = $file.FullName.Substring($packageRoot.Length + 1).Replace('\', '/')
    $bytes = [IO.File]::ReadAllBytes($file.FullName)
    $textViews = @([Text.Encoding]::UTF8.GetString($bytes), [Text.Encoding]::Unicode.GetString($bytes))
    # UTF-16 strings inside a binary may start at either byte alignment.
    if ($bytes.Length -gt 1) { $textViews += [Text.Encoding]::Unicode.GetString($bytes, 1, $bytes.Length - 1) }
    foreach ($value in $textViews) {
        if ($value -match '(?i)(?:[A-Z]:[\\/]+(?:Users|home)[\\/]+|/(?:Users|home)/)' -or
            ($env:USERPROFILE -and $value.IndexOf($env:USERPROFILE, [StringComparison]::OrdinalIgnoreCase) -ge 0) -or
            ($env:USERNAME.Length -gt 2 -and $value.IndexOf($env:USERNAME, [StringComparison]::OrdinalIgnoreCase) -ge 0)) {
            throw "Personal path or local user name found in $relative. No zip was created."
        }
    }
    '{0} ({1} bytes)' -f $relative, $file.Length
}
$inventory | Set-Content -LiteralPath (Join-Path $runRoot 'contents.txt') -Encoding UTF8
$inventory | ForEach-Object { Write-Output $_ }

[xml]$props = Get-Content -LiteralPath (Join-Path $repoRoot 'Directory.Build.props')
$version = $props.SelectSingleNode('//Version').InnerText
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected a three-part release version.' }
$suffix = if ($FrameworkDependent) { '-framework-dependent-check' } else { '' }
$zipPath = Join-Path $runRoot "HnMemo-$version-win-x64$suffix.zip"
Compress-Archive -LiteralPath $packageRoot -DestinationPath $zipPath -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
@("Version: $version", "Self-contained: $(-not $FrameworkDependent)", "Files: $($files.Count)", 'Allowlist: PASS; no work records, tests, PDBs or personal settings copied.',
    'Personal path/user name search: PASS (UTF-8 and both UTF-16 byte alignments, all packaged files).',
    "Zip SHA256: $hash") | Set-Content -LiteralPath (Join-Path $runRoot 'verification.txt') -Encoding UTF8
Write-Output "ZIP: $zipPath"
Write-Output "SHA256: $hash"
