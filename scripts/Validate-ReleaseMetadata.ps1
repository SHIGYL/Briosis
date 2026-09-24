param(
    [string]$Tag
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'Brio/Briosis.csproj'
$manifestPath = Join-Path $repositoryRoot 'Brio/Briosis.json'
$repositoryManifestPath = Join-Path $repositoryRoot 'repo.json'

[xml]$project = Get-Content -LiteralPath $projectPath
$versionNode = $project.SelectSingleNode('/Project/PropertyGroup/Version')
if ($null -eq $versionNode) {
    throw 'Brio/Briosis.csproj does not define Version.'
}

$version = $versionNode.InnerText
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$repositoryEntries = @(Get-Content -LiteralPath $repositoryManifestPath -Raw | ConvertFrom-Json)

if ($repositoryEntries.Count -ne 1) {
    throw "repo.json must contain exactly one entry; found $($repositoryEntries.Count)."
}

$repositoryEntry = $repositoryEntries[0]
$checks = [ordered]@{
    'manifest Name' = [string]$manifest.Name
    'manifest InternalName' = [string]$manifest.InternalName
    'repository Name' = [string]$repositoryEntry.Name
    'repository InternalName' = [string]$repositoryEntry.InternalName
}

foreach ($check in $checks.GetEnumerator()) {
    if ($check.Value -ne 'Briosis') {
        throw "$($check.Key) must be Briosis; found '$($check.Value)'."
    }
}

if ([string]$manifest.AssemblyVersion -ne $version) {
    throw "Manifest version $($manifest.AssemblyVersion) does not match project version $version."
}

if ([string]$repositoryEntry.AssemblyVersion -ne $version) {
    throw "Repository version $($repositoryEntry.AssemblyVersion) does not match project version $version."
}

if ([int]$manifest.DalamudApiLevel -ne 15 -or [int]$repositoryEntry.DalamudApiLevel -ne 15) {
    throw 'Manifest and repo.json must both target Dalamud API level 15.'
}

$expectedAssetUrl = "https://github.com/SHIGYL/Briosis/releases/download/v$version/Briosis.zip"
if ([string]$repositoryEntry.DownloadLinkInstall -ne $expectedAssetUrl -or
    [string]$repositoryEntry.DownloadLinkUpdate -ne $expectedAssetUrl) {
    throw "repo.json release URLs must both be $expectedAssetUrl."
}

$expectedIconUrl = 'https://raw.githubusercontent.com/SHIGYL/Briosis/main/Resources/Images/BriosisIcon.png'
if ([string]$manifest.IconUrl -ne $expectedIconUrl -or
    [string]$repositoryEntry.IconUrl -ne $expectedIconUrl) {
    throw "Manifest and repo.json icon URLs must both be $expectedIconUrl."
}

if ($Tag -and $Tag -ne "v$version") {
    throw "Tag $Tag does not match project version v$version."
}

Write-Output "Release metadata is consistent: Briosis $version, Dalamud API 15."
