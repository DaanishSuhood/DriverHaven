# Builds DriverHaven for release:
#   .\publish.ps1                 -> artifacts\publish\DriverHaven.exe (one self-contained file, no .NET install needed)
#                                    artifacts\installer\DriverHaven-Setup-<version>.exe (Inno Setup installer)
#   .\publish.ps1 -SkipInstaller  -> only the .exe
# Either way it also writes artifacts\SHA256SUMS.txt, to publish with the release.
# Trimming stays off: the Windows Update code uses COM through `dynamic`, which trimming breaks.
# ContinuousIntegrationBuild records source paths as /_/... so the build machine's folders don't end up in the exe.
param(
    [string]$Runtime = 'win-x64',
    [switch]$SkipInstaller
)
$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'src\DriverUpdater.App\DriverUpdater.App.csproj'
$publishDir = Join-Path $PSScriptRoot 'artifacts\publish'

dotnet test (Join-Path $PSScriptRoot 'tests\DriverUpdater.Core.Tests') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Tests failed; not publishing.' }

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

dotnet publish $project -c Release -r $Runtime --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=embedded `
    -p:ContinuousIntegrationBuild=true `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

if (-not $SkipInstaller) {
    $iscc = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) {
        throw 'Inno Setup 6 was not found. Install it with: winget install --id JRSoftware.InnoSetup -e (or use -SkipInstaller).'
    }

    $version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    & $iscc /Q "/DAppVersion=$version" (Join-Path $PSScriptRoot 'installer\DriverHaven.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Building the installer failed.' }
    $installer = Join-Path $PSScriptRoot "artifacts\installer\DriverHaven-Setup-$version.exe"
}

$artifacts = Join-Path $PSScriptRoot 'artifacts'
# Only what this run built: artifacts\installer may still hold installers from earlier versions.
$files = @(Get-Item (Join-Path $publishDir 'DriverHaven.exe')) + @(if (-not $SkipInstaller) { Get-Item $installer })

# Releases aren't code-signed, so publish checksums people can compare against
# (sha256sum format: check with `sha256sum -c SHA256SUMS.txt`, or compare with Get-FileHash).
$checksums = Join-Path $artifacts 'SHA256SUMS.txt'
$lines = $files | ForEach-Object { "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $($_.Name)" }
# LF line endings: sha256sum on Linux, macOS and Git Bash treats a CR as part of the file name.
[IO.File]::WriteAllText($checksums, (($lines -join "`n") + "`n"))
$lines

$files |
    Format-Table @{ n = 'File'; e = { $_.FullName.Substring($PSScriptRoot.Length + 1) } },
                 @{ n = 'Size (MB)'; e = { [math]::Round($_.Length / 1MB, 1) } }
