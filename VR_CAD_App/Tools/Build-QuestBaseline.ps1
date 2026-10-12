[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][ValidateSet('Link','Android')][string]$Target,
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\2022.3.44f1\Editor\Unity.exe'
)
$ErrorActionPreference = 'Stop'
$sourceProject = [IO.Path]::GetFullPath((Split-Path $PSScriptRoot -Parent))
if ($sourceProject -match '[\\/]\.baseline-workspace[\\/]') { throw 'Run this wrapper from the original project only.' }
$repoRoot = Split-Path $sourceProject -Parent
$copyRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot '.baseline-workspace'))
$buildProject = [IO.Path]::GetFullPath((Join-Path $copyRoot 'VR_CAD_App'))
if ($buildProject -eq $sourceProject -or !$buildProject.StartsWith($copyRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Unsafe build-copy path.'
}
New-Item -ItemType Directory -Force $buildProject | Out-Null
$lockPath = Join-Path $buildProject 'Temp\UnityLockfile'
if (Test-Path $lockPath) {
    try { $probe = [IO.File]::Open($lockPath,'Open','ReadWrite','None'); $probe.Dispose() }
    catch { throw 'A build is already using the isolated copy. Wait for it to finish before synchronizing.' }
}

# Mirror only source inputs in the disposable copy. Never copy anything back.
# Resolve and check every deletion-capable /MIR destination before invoking robocopy.
foreach ($folder in @('Assets','Packages','ProjectSettings','Tools')) {
    $source = [IO.Path]::GetFullPath((Join-Path $sourceProject $folder))
    $destination = [IO.Path]::GetFullPath((Join-Path $buildProject $folder))
    if (!$destination.StartsWith($buildProject + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase) -or $destination -eq $source) {
        throw "Unsafe synchronization target: $destination"
    }
    & robocopy.exe $source $destination /MIR /XJ /R:1 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -gt 7) { throw "Synchronization failed for $folder (robocopy $LASTEXITCODE)." }
}

$records = foreach ($folder in @('Assets','Packages','ProjectSettings','Tools')) {
    $source = Join-Path $sourceProject $folder
    $sourceFiles = @(Get-ChildItem -LiteralPath $source -Recurse -File)
    $copyFiles = @(Get-ChildItem -LiteralPath (Join-Path $buildProject $folder) -Recurse -File)
    if ($sourceFiles.Count -ne $copyFiles.Count) { throw "File count changed while synchronizing $folder. Retry after saving." }
    foreach ($file in $sourceFiles) {
        $relative = $file.FullName.Substring($sourceProject.Length + 1)
        $sourceHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        $copyHash = (Get-FileHash -LiteralPath (Join-Path $buildProject $relative) -Algorithm SHA256).Hash
        if ($sourceHash -ne $copyHash) { throw "Source changed during synchronization: $relative. Retry after saving." }
        [ordered]@{ path=$relative; sha256=$sourceHash }
    }
}
$reportRoot = Join-Path $buildProject 'Logs\QuestBaseline'
New-Item -ItemType Directory -Force $reportRoot | Out-Null
[ordered]@{ sourceProject=$sourceProject; buildProject=$buildProject; utc=[DateTime]::UtcNow.ToString('o'); files=@($records) } |
    ConvertTo-Json -Depth 5 | Set-Content (Join-Path $reportRoot ("source-$Target.json")) -Encoding UTF8
[ordered]@{ sourceProject=$sourceProject; buildProject=$buildProject } |
    ConvertTo-Json | Set-Content (Join-Path $buildProject 'baseline-copy.json') -Encoding UTF8

Write-Host "Synchronized and hashed saved source: $sourceProject -> $buildProject"
Write-Host 'The original editor and its build settings are untouched. Build reports and artifacts stay in the copy.'
$runner = Join-Path $buildProject 'Tools\QuestBaseline.ps1'
$action = if ($Target -eq 'Android') { 'BuildAndroid' } else { 'BuildLink' }
& powershell.exe -NoProfile -File $runner -Action $action -UnityPath $UnityPath
exit $LASTEXITCODE
