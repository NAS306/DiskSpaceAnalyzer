param([switch]$FrameworkDependent)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$target = Join-Path $projectRoot $(if ($FrameworkDependent) { 'dist-framework' } else { 'dist' })
$self = if ($FrameworkDependent) { 'false' } else { 'true' }
$staging = Join-Path $projectRoot ("artifacts/publish-" + [Guid]::NewGuid().ToString('N'))
$project = Join-Path $projectRoot 'src/DiskSpaceAnalyzer.csproj'
$sdk = Join-Path $projectRoot 'tools/dotnet/dotnet.exe'
if (-not (Test-Path -LiteralPath $sdk)) { $sdk = 'dotnet' }
& $sdk publish $project -c Release -r win-x64 --self-contained $self -o $staging -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Publish failed. Existing dist files were preserved.' }
New-Item -ItemType Directory -Force -Path $target | Out-Null
foreach ($file in Get-ChildItem -LiteralPath $staging -File -Recurse) {
    $relative = $file.FullName.Substring($staging.Length).TrimStart('\')
    $destination = Join-Path $target $relative
    New-Item -ItemType Directory -Force -Path (Split-Path $destination) | Out-Null
    if (Test-Path -LiteralPath $destination) {
        $backup = Join-Path (Split-Path $destination) ($file.BaseName + '_BU' + $file.Extension)
        Copy-Item -LiteralPath $destination -Destination $backup -Force
    }
    Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
}
Write-Host "Built: $target\DiskSpaceAnalyzer.exe"
$archive = Join-Path $projectRoot $(if ($FrameworkDependent) { 'DiskSpaceAnalyzer-framework-win-x64.zip' } else { 'DiskSpaceAnalyzer-win-x64.zip' })
if (Test-Path -LiteralPath $archive) {
    $archiveItem = Get-Item -LiteralPath $archive
    Copy-Item -LiteralPath $archive -Destination (Join-Path $projectRoot ($archiveItem.BaseName + '_BU.zip')) -Force
}
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $archive -Force
Write-Host "Package: $archive"
$infoPath = Join-Path $projectRoot 'artifacts/publish-info.json'
if (Test-Path -LiteralPath $infoPath) { Copy-Item -LiteralPath $infoPath -Destination (Join-Path $projectRoot 'artifacts/publish-info_BU.json') -Force }
@{ Staging = $staging; SelfContained = -not $FrameworkDependent; Archive = $archive; Version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version } | ConvertTo-Json | Set-Content -LiteralPath $infoPath -Encoding UTF8
