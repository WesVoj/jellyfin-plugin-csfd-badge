param(
    [string]$ManifestPath = "artifacts/manifest.json"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$resolvedManifestPath = Join-Path $root $ManifestPath
$json = [IO.File]::ReadAllText($resolvedManifestPath, [Text.Encoding]::UTF8)

if (-not $json.TrimStart().StartsWith("[", [StringComparison]::Ordinal)) {
    throw "Plugin manifest root must be a JSON array."
}

$packages = @($json | ConvertFrom-Json)
if ($packages.Count -ne 1) {
    throw "Plugin manifest must contain exactly one package."
}

$package = $packages[0]
if ($package.guid -ne "93e08fe1-3db0-43bc-99fb-6fdbe8fe51e4") {
    throw "Plugin manifest contains an unexpected GUID."
}

if (@($package.versions).Count -lt 1) {
    throw "Plugin manifest does not contain a version."
}

$version = @($package.versions)[0]
$zipName = [IO.Path]::GetFileName([Uri]$version.sourceUrl)
$zipPath = Join-Path (Split-Path -Parent $resolvedManifestPath) $zipName
if (-not (Test-Path -LiteralPath $zipPath)) {
    throw "Package referenced by the manifest was not built: $zipName"
}

$checksum = (Get-FileHash -LiteralPath $zipPath -Algorithm MD5).Hash.ToLowerInvariant()
if ($checksum -ne $version.checksum) {
    throw "Plugin manifest checksum does not match $zipName."
}

Write-Host "Manifest is valid: $($package.name) $($version.version)"
