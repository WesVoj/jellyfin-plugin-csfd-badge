param(
    [string]$Configuration = "Release",
    [string]$Repository = "WesVoj/jellyfin-plugin-csfd-badge",
    [string]$ReleaseTag
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src/Jellyfin.Plugin.CsfdBadge/Jellyfin.Plugin.CsfdBadge.csproj"
$artifactRoot = Join-Path $root "artifacts"
$publishDirectory = Join-Path $artifactRoot "plugin"
[xml]$buildProperties = Get-Content -LiteralPath (Join-Path $root "Directory.Build.props")
$version = [string]$buildProperties.Project.PropertyGroup.Version
$expectedReleaseTag = "v$version"
if ($ReleaseTag -and $ReleaseTag -ne $expectedReleaseTag) {
    throw "Release tag '$ReleaseTag' does not match project version '$version'. Expected '$expectedReleaseTag'."
}
$packagePath = Join-Path $artifactRoot "Jellyfin.Plugin.CsfdBadge_$version.zip"
$manifestPath = Join-Path $artifactRoot "manifest.json"
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
$releaseTagValue = if ($ReleaseTag) { $ReleaseTag } else { $expectedReleaseTag }
$packageName = Split-Path -Leaf $packagePath
$csfdName = "$([char]0x010C)SFD"
$manifest = @(
    [ordered]@{
        name = "$csfdName Badge"
        guid = "93e08fe1-3db0-43bc-99fb-6fdbe8fe51e4"
        overview = "Cached, clickable $csfdName ratings for Jellyfin."
        description = "Adds a cached, clickable $csfdName percentage to movie and series detail pages."
        owner = "WesVoj"
        category = "Metadata"
        manifestVersion = 1
        readmeUrl = "https://raw.githubusercontent.com/$Repository/main/README.md"
        status = "Stable"
        versions = @(
            [ordered]@{
                version = $version
                changelog = "See https://github.com/$Repository/releases/tag/$releaseTagValue"
                targetAbi = "10.11.0.0"
                sourceUrl = "https://github.com/$Repository/releases/download/$releaseTagValue/$packageName"
                checksum = $hash.Hash.ToLowerInvariant()
                timestamp = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ")
            }
        )
    }
)
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8
Write-Host "Package: $packagePath"
Write-Host "MD5: $($hash.Hash.ToLowerInvariant())"
Write-Host "Manifest: $manifestPath"
