[CmdletBinding()]
param(
    [ValidateSet('Preflight','Configure','ValidateAndroid','ValidateLink','BuildAndroid','BuildLink','Install','Collect','RecordCase')]
    [string]$Action = 'Preflight',
    [string]$UnityPath = 'C:\Program Files\Unity\Hub\Editor\2022.3.44f1\Editor\Unity.exe',
    [string]$Serial,
    [ValidateSet('Quest2-Standalone','QuestLink-PC')][string]$Mode = 'Quest2-Standalone',
    [ValidateSet('Tracking','ControllerInput','Grabbing','UI','Export')][string]$Case,
    [ValidateSet('Pass','Fail','Blocked')][string]$Result,
    [string]$Notes,
    [string]$Evidence
)
$ErrorActionPreference = 'Stop'
$projectPath = Split-Path $PSScriptRoot -Parent
$reportRoot = Join-Path $projectPath 'Logs\QuestBaseline'
$packageId = 'com.vrcad.questbaseline'
New-Item -ItemType Directory -Force $reportRoot | Out-Null
$adbPath = Join-Path (Split-Path $UnityPath -Parent) 'Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe'

function Invoke-Adb {
    param([string[]]$AdbArguments)
    $output = & $adbPath @AdbArguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "ADB failed ($LASTEXITCODE): $output" }
    return $output
}

function Get-QuestSerial {
    $lines = Invoke-Adb -AdbArguments @('devices','-l')
    $ready = @($lines | ForEach-Object { if ("$_" -match '^(\S+)\s+device\s') { $Matches[1] } })
    if ($Serial) {
        if ($Serial -notin $ready) { throw 'Requested device is absent or unauthorized. Accept USB debugging in the headset.' }
        $selectedSerial = $Serial
    } else {
        if ($ready.Count -ne 1) { throw 'Connect exactly one authorized Quest 2, or specify -Serial.' }
        $selectedSerial = $ready[0]
    }
    $model = (Invoke-Adb -AdbArguments @('-s',$selectedSerial,'shell','getprop','ro.product.model')) -join ''
    if ($model.Trim() -notmatch '(?i)Quest\s*2') { throw "Expected Quest 2; connected model is '$model'." }
    return $selectedSerial
}

if ($Action -eq 'Preflight') {
    $devices = if (Test-Path $adbPath) { (Invoke-Adb -AdbArguments @('devices','-l')) -join "`n" } else { 'ADB missing' }
    $runtime = (Get-ItemProperty 'HKLM:\SOFTWARE\Khronos\OpenXR\1' -ErrorAction SilentlyContinue).ActiveRuntime
    $report = [ordered]@{
        utc = [DateTime]::UtcNow.ToString('o'); unityInstalled = (Test-Path $UnityPath)
        androidToolsInstalled = (Test-Path $adbPath); devices = $devices
        windowsOpenXRRuntime = $runtime
        note = 'Presence checks only. A license, successful builds and headset acceptance are still required.'
    }
    $report | ConvertTo-Json | Set-Content (Join-Path $reportRoot 'preflight.json') -Encoding UTF8
    $report | Format-List
    exit 0
}

if ($Action -in @('Configure','ValidateAndroid','ValidateLink','BuildAndroid','BuildLink')) {
    if (!(Test-Path (Join-Path $projectPath 'baseline-copy.json'))) {
        throw 'Run Tools/Build-QuestBaseline.ps1 from the original project. Batch target/configuration changes are restricted to its isolated copy.'
    }
    if (!(Test-Path $UnityPath)) { throw "Unity missing: $UnityPath" }
    $lockPath = Join-Path $projectPath 'Temp\UnityLockfile'
    if (Test-Path $lockPath) {
        try { $lockProbe = [IO.File]::Open($lockPath, 'Open', 'ReadWrite', 'None'); $lockProbe.Dispose() }
        catch { throw 'This project is open in Unity. Save/close it, use the CAD menu, or build an isolated project copy.' }
    }
    $target = if ($Action -match 'Android') { 'Android' } else { 'Win64' }
    $method = switch ($Action) {
        'Configure' { 'Configure' }
        'ValidateAndroid' { 'Validate' }
        'ValidateLink' { 'Validate' }
        'BuildAndroid' { 'BuildAndroid' }
        'BuildLink' { 'BuildLink' }
    }
    $log = Join-Path $reportRoot ($Action + '.log')
    $arguments = '-batchmode -nographics -quit -projectPath "{0}" -buildTarget {1} -executeMethod CADApp.EditorTools.QuestBaselineBuild.{2} -logFile "{3}"' -f $projectPath,$target,$method,$log
    $process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    Write-Host "Unity PID $($process.Id). Log: $log"
    $process.WaitForExit()
    $process.Refresh()
    $exitCode = $process.ExitCode
    [ordered]@{ utc=[DateTime]::UtcNow.ToString('o'); action=$Action; exitCode=$exitCode; log=$log } |
        ConvertTo-Json | Set-Content (Join-Path $reportRoot ($Action + '-process.json')) -Encoding UTF8
    if ($exitCode -ne 0) { throw "Unity failed with exit code $exitCode. Inspect $log" }
    Write-Host "Unity completed $Action. Inspect validation/build reports before recording acceptance."
    exit 0
}

if ($Action -eq 'Install') {
    $deviceSerial = Get-QuestSerial
    $apk = Join-Path $projectPath 'Builds\Quest2\VR_CAD_Quest2.apk'
    if (!(Test-Path $apk)) { throw 'APK missing. Run BuildAndroid first.' }
    $install = Invoke-Adb -AdbArguments @('-s',$deviceSerial,'install','-r',$apk)
    $launch = Invoke-Adb -AdbArguments @('-s',$deviceSerial,'shell','am','start','-W','-n',"$packageId/com.unity3d.player.UnityPlayerActivity")
    $report = @("UTC: $([DateTime]::UtcNow.ToString('o'))", "APK SHA256: $((Get-FileHash $apk -Algorithm SHA256).Hash)") + $install + $launch
    $report | Set-Content (Join-Path $reportRoot 'install-standalone.txt') -Encoding UTF8
    if (($launch -join "`n") -match 'Error:|Exception|Status:\s*(?!ok)\w+') { throw 'Android launch failed; inspect install-standalone.txt.' }
    $report
    exit 0
}

if ($Action -eq 'Collect') {
    $destination = Join-Path $reportRoot ("Evidence\{0}\{1}" -f $Mode,[DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))
    New-Item -ItemType Directory -Force $destination | Out-Null
    if ($Mode -eq 'Quest2-Standalone') {
        $deviceSerial = Get-QuestSerial
        $appRoot = "/sdcard/Android/data/$packageId/files"
        Invoke-Adb -AdbArguments @('-s',$deviceSerial,'pull',"$appRoot/QuestBaseline",$destination)
        Invoke-Adb -AdbArguments @('-s',$deviceSerial,'pull',"$appRoot/CAD_Exports",$destination)
        Invoke-Adb -AdbArguments @('-s',$deviceSerial,'logcat','-d','-s','Unity','AndroidRuntime') |
            Set-Content (Join-Path $destination 'device-logcat.txt') -Encoding UTF8
    } else {
        # Derive Unity's persistentDataPath from the saved company/product settings.
        $settings = Get-Content (Join-Path $projectPath 'ProjectSettings\ProjectSettings.asset') -Raw
        $company = [regex]::Match($settings,'(?m)^  companyName: (.+)$').Groups[1].Value.Trim()
        $product = [regex]::Match($settings,'(?m)^  productName: (.+)$').Groups[1].Value.Trim()
        if (!$company -or !$product -or $company -match '[/\\]' -or $product -match '[/\\]') { throw 'Cannot resolve desktop evidence path.' }
        $persistentPath = Join-Path (Split-Path $env:LOCALAPPDATA -Parent) "LocalLow\$company\$product"
        foreach ($folder in @('QuestBaseline','CAD_Exports')) {
            $source = Join-Path $persistentPath $folder
            if (!(Test-Path $source)) { throw "Evidence missing: $source. Run the app and export first." }
            Copy-Item -LiteralPath $source -Destination $destination -Recurse
        }
        $playerLog = Join-Path $persistentPath 'Player.log'
        if (Test-Path $playerLog) { Copy-Item -LiteralPath $playerLog -Destination $destination }
    }
    Write-Host "Evidence collected: $destination. Collection does not mark acceptance tests as passed."
    exit 0
}

if ($Action -eq 'RecordCase') {
    if (!$Case -or !$Result -or !$Notes) { throw 'RecordCase requires -Mode, -Case, -Result and -Notes.' }
    if ($Result -eq 'Pass' -and (!$Evidence -or !(Test-Path -LiteralPath $Evidence))) {
        throw 'A passing result requires -Evidence pointing to an existing captured file or directory.'
    }
    $entry = [ordered]@{
        utc=[DateTime]::UtcNow.ToString('o'); mode=$Mode; testCase=$Case; result=$Result
        notes=$Notes; evidence=$Evidence; source='Manual tester observation'
    }
    $entry | ConvertTo-Json -Compress | Add-Content (Join-Path $reportRoot 'acceptance.jsonl') -Encoding UTF8
    $entry | Format-List
}
