param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src/Jellyfin.Plugin.CsfdBadge/Jellyfin.Plugin.CsfdBadge.csproj"
$artifactRoot = Join-Path $root "artifacts"
$publishDirectory = Join-Path $artifactRoot "plugin"
[xml]$buildProperties = Get-Content -LiteralPath (Join-Path $root "Directory.Build.props")
$version = [string]$buildProperties.Project.PropertyGroup.Version
$packagePath = Join-Path $artifactRoot "Jellyfin.Plugin.CsfdBadge_$version.zip"
$artifactRootFull = [IO.Path]::GetFullPath($artifactRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
$publishDirectoryFull = [IO.Path]::GetFullPath($publishDirectory)

if (-not $publishDirectoryFull.StartsWith(
        $artifactRootFull + [IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean a publish directory outside the workspace artifact directory."
}

if (Test-Path -LiteralPath $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $publishDirectory | Out-Null
dotnet publish $project -c $Configuration -o $publishDirectory

if (Test-Path -LiteralPath $packagePath) {
    Remove-Item -LiteralPath $packagePath -Force
}

Compress-Archive -Path (Join-Path $publishDirectory "*") -DestinationPath $packagePath
$hash = Get-FileHash -LiteralPath $packagePath -Algorithm MD5
Write-Host "Package: $packagePath"
Write-Host "MD5: $($hash.Hash.ToLowerInvariant())"
