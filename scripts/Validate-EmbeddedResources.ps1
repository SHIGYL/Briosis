param(
    [string]$AssemblyPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $AssemblyPath) {
    $AssemblyPath = Join-Path $repositoryRoot 'Brio/bin/x64/Release/Briosis.dll'
}

$resolvedAssemblyPath = (Resolve-Path -LiteralPath $AssemblyPath).Path
$assembly = [System.Reflection.Assembly]::LoadFile($resolvedAssemblyPath)
$resourceNames = @($assembly.GetManifestResourceNames())

$requiredResources = @(
    'Brio.Resources.Embedded.Language.en.json',
    'Brio.Resources.Embedded.Data.PathStore.bpath',
    'Brio.Resources.Embedded.Data.WorldObjectPaths.json.gz',
    'Brio.Resources.Embedded.Images.IconBorder.png',
    'Brio.Resources.Embedded.Changelog.changelog.yaml'
)

$missingResources = @($requiredResources | Where-Object { $resourceNames -notcontains $_ })
if ($missingResources.Count -gt 0) {
    throw "Briosis.dll is missing required embedded resources: $($missingResources -join ', ')."
}

$wrongPrefixResources = @($resourceNames | Where-Object { $_.StartsWith('Briosis.Resources.Embedded.', [System.StringComparison]::Ordinal) })
if ($wrongPrefixResources.Count -gt 0) {
    throw "Briosis.dll contains embedded resources with the wrong Briosis prefix: $($wrongPrefixResources -join ', ')."
}

Write-Output "Embedded resource identity is valid: $($requiredResources.Count) critical resources use the Brio prefix."
