param([switch]$SkipPublish)
$ErrorActionPreference = 'Stop'
if (-not $SkipPublish) { & (Join-Path $PSScriptRoot 'build.ps1') }
$info = Get-Content -Encoding UTF8 (Join-Path $PSScriptRoot 'artifacts/publish-info.json') -Raw | ConvertFrom-Json
if (-not $info.SelfContained) { throw 'The installer requires a self-contained publish. Run build.ps1 without -FrameworkDependent.' }
$staging = [IO.Path]::GetFullPath($info.Staging)
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'artifacts')) + '\'
if (-not $staging.StartsWith($artifactRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Publish path is outside the workspace artifacts directory.' }
$compiler = Join-Path $PSScriptRoot 'tools/InnoSetup/ISCC.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Install Inno Setup 6.7.4 or later into tools/InnoSetup. See installer/README.md.' }
$dependency = Join-Path $PSScriptRoot 'installer/vendor/Everything-1.4.1.1032.x64-Setup.exe'
$expected = 'C42EFAD041D4C0BB4D4AC97AE7CBE89F153EC1FE078772392E749C7F5D5282D3'
if ((Get-FileHash -LiteralPath $dependency -Algorithm SHA256).Hash -ne $expected) { throw 'Official Everything dependency SHA-256 mismatch.' }
$destination = Join-Path $PSScriptRoot 'DiskSpaceAnalyzer-Setup.exe'
if (Test-Path -LiteralPath $destination) { Copy-Item -LiteralPath $destination -Destination (Join-Path $PSScriptRoot 'DiskSpaceAnalyzer-Setup_BU.exe') -Force }
& $compiler "/DPublishDir=$staging" "/DOutputDir=$PSScriptRoot" (Join-Path $PSScriptRoot 'installer/DiskSpaceAnalyzer.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
Write-Host "Installer: $destination"
