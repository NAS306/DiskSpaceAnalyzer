$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$testRoot = Join-Path $projectRoot 'artifacts/installer-smoke'
$install = Join-Path $testRoot 'app'
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
$setup = Join-Path $projectRoot 'DiskSpaceAnalyzer-Setup.exe'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{7D827938-6AC4-445C-9035-42AB2885499A}_is1'
if (Test-Path -LiteralPath $uninstallKey) {
    $existing = (Get-ItemProperty -LiteralPath $uninstallKey).InstallLocation.TrimEnd('\')
    if (-not $existing.Equals($install, [StringComparison]::OrdinalIgnoreCase)) { throw 'A real installation exists. Run this smoke test on a test account or VM.' }
}
$preferences = 'HKCU:\Software\DiskSpaceAnalyzer'
$originalPath = Join-Path $testRoot 'original-preferences.json'
if (-not (Test-Path -LiteralPath $originalPath)) {
    $original = @{ Exists = (Test-Path -LiteralPath $preferences); Language = $null; EverythingPath = $null }
    if ($original.Exists) { $value = Get-ItemProperty -LiteralPath $preferences; $original.Language = $value.Language; $original.EverythingPath = $value.EverythingPath }
    $original | ConvertTo-Json | Set-Content -LiteralPath $originalPath -Encoding UTF8
}
$original = Get-Content -LiteralPath $originalPath -Encoding UTF8 -Raw | ConvertFrom-Json
$checks = New-Object 'System.Collections.Generic.List[string]'
function Check([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
    $checks.Add($message); Write-Host "PASS $message"
}
function RunHidden([string]$file, [string[]]$arguments) {
    $process = Start-Process -FilePath $file -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "$file exited with $($process.ExitCode)" }
}
$dataRoot = Join-Path $env:LOCALAPPDATA 'DiskSpaceAnalyzer'
[IO.Directory]::CreateDirectory($dataRoot) | Out-Null
$sentinel = Join-Path $dataRoot ('installer-smoke-' + [Guid]::NewGuid().ToString('N') + '.txt')
[IO.File]::WriteAllText($sentinel, 'Uninstall must preserve user data.')
$sentinelHash = (Get-FileHash -LiteralPath $sentinel -Algorithm SHA256).Hash
$everything = Join-Path $env:ProgramFiles 'Everything/Everything.exe'
$everythingHash = (Get-FileHash -LiteralPath $everything -Algorithm SHA256).Hash
$explorerKeys = @('Software\Classes\Directory\shell\DiskSpaceAnalyzer.Analyze', 'Software\Classes\Directory\Background\shell\DiskSpaceAnalyzer.Analyze')
$exports = @{}
$reg = Join-Path $env:WINDIR 'System32/reg.exe'
for ($i=0; $i -lt $explorerKeys.Count; $i++) {
    if (Test-Path -LiteralPath ('HKCU:\' + $explorerKeys[$i])) {
        $path = Join-Path $testRoot ("explorer-$i.reg")
        & $reg export ('HKCU\' + $explorerKeys[$i]) $path /y | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Cannot back up Explorer registration.' }
        $exports[$explorerKeys[$i]] = $path
    }
}
try {
    foreach ($language in @('english','korean')) {
        $log = Join-Path $testRoot ("install-$language-final.log")
        RunHidden $setup @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/LANG='+$language),('/DIR="'+$install+'"'),('/LOG="'+$log+'"'))
        $text = Get-Content -LiteralPath $log -Encoding UTF8 -Raw
        Check ($text.Contains('Reusing Everything:') -and -not $text.Contains('Attempting WinGet')) "$language installation reuses Everything without reinstalling it"
        Check ($text.Contains('Verified Everything IPC search through the installed application.')) "$language installation verifies an actual IPC search"
        Check (-not (Test-Path -LiteralPath (Join-Path $install 'Everything/Everything.exe'))) 'Installer excludes the portable engine'
        Check ((Get-Content -LiteralPath (Join-Path $install 'DiskSpaceAnalyzer.runtimeconfig.json') -Raw).Contains('net10.0')) '.NET 10 runtime configuration is installed'
        $report = Join-Path $testRoot ("language-$language.json")
        RunHidden (Join-Path $install 'DiskSpaceAnalyzer.exe') @('--check-everything','--output',('"'+$report+'"'))
        $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
        $expected = if ($language -eq 'korean') { 'ko' } else { 'en' }
        Check ($result.Ready -and $result.Language -eq $expected) "$language preference survives application restart"
    }
    # One menu belongs to this installation; the other deliberately points elsewhere.
    $owned = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($explorerKeys[0]+'\command')
    $owned.SetValue('', '"'+(Join-Path $install 'DiskSpaceAnalyzer.exe')+'" --root "%1\."'); $owned.Dispose()
    $foreignCommand = '"'+(Join-Path $projectRoot 'dist/DiskSpaceAnalyzer.exe')+'" --root "%V\."'
    $foreign = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($explorerKeys[1]+'\command')
    $foreign.SetValue('', $foreignCommand); $foreign.Dispose()
    $uninstallLog = Join-Path $testRoot 'uninstall-final.log'
    RunHidden (Join-Path $install 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="'+$uninstallLog+'"'))
    Check (-not (Test-Path -LiteralPath (Join-Path $install 'DiskSpaceAnalyzer.exe'))) 'Uninstall removes the installed application'
    Check (-not (Test-Path -LiteralPath $uninstallKey)) 'Uninstall removes its Apps and Features entry'
    Check (-not (Test-Path -LiteralPath ('HKCU:\'+$explorerKeys[0]))) 'Uninstall removes its own Explorer menu'
    $foreign = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($explorerKeys[1]+'\command')
    try { Check ($foreign.GetValue('') -eq $foreignCommand) 'Uninstall preserves an Explorer menu owned by another app location' } finally { $foreign.Dispose() }
    Check ((Get-FileHash -LiteralPath $everything -Algorithm SHA256).Hash -eq $everythingHash) 'Install and uninstall preserve the existing Everything binary'
    Check ((Get-FileHash -LiteralPath $sentinel -Algorithm SHA256).Hash -eq $sentinelHash) 'Uninstall preserves existing user data'
    Check ((Get-ItemProperty -LiteralPath $preferences).Language -eq 'ko') 'Uninstall preserves language preferences'
    @{ Passed = $true; Checks = $checks; CleanMachineDependencyInstallTested = $false } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $testRoot 'result.json') -Encoding UTF8
} finally {
    foreach ($key in $explorerKeys) {
        [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($key, $false)
        if ($exports.ContainsKey($key)) { & $reg import $exports[$key] | Out-Null }
    }
    if (Test-Path -LiteralPath $sentinel) { Remove-Item -LiteralPath $sentinel }
    foreach ($name in @('Language','EverythingPath')) {
        if ($null -ne $original.$name) { Set-ItemProperty -LiteralPath $preferences -Name $name -Value $original.$name }
        else { Remove-ItemProperty -LiteralPath $preferences -Name $name -ErrorAction SilentlyContinue }
    }
    if (-not $original.Exists -and (Test-Path -LiteralPath $preferences)) {
        $key = Get-Item -LiteralPath $preferences
        if ($key.ValueCount -eq 0 -and $key.SubKeyCount -eq 0) { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey('Software\DiskSpaceAnalyzer', $false) }
    }
}
