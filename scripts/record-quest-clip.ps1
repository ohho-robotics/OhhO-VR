# OHH-60: timed Quest screen capture, then pull the mp4 into a dated folder.
# Talks to adb only (devices, getprop, screenrecord, pull, rm of that mp4).
# Does not start a robot, open a robot socket, or publish a command.
# --dry-run prints the plan and does not call adb.

$ErrorActionPreference = "Continue"

$Size = "1920x1080"
$Bitrate = "8000000"
$Remote = "/sdcard/ohh60-quest-teleop.mp4"
$SecondsLimit = 45
$DryRun = $false
$Serial = ""
$OutParent = ""

function Show-Usage {
    Write-Output @"
Usage: scripts/record-quest-clip.ps1 [--dry-run] [--seconds 30-60] [--serial SERIAL] [--out DIR]

Checks that adb devices shows a Quest, records the headset screen for the
given number of seconds, and pulls the mp4 into a UTC-dated folder.
--dry-run prints those commands and exits. It does not call adb.
"@
}

function Fail-Clip([string]$Message, [int]$Code = 1) {
    [Console]::Error.WriteLine("record-quest-clip: $Message")
    exit $Code
}

$index = 0
while ($index -lt $args.Count) {
    $arg = [string]$args[$index]
    switch ($arg) {
        "--dry-run" { $DryRun = $true; $index++ }
        "--help" { Show-Usage; exit 0 }
        "-h" { Show-Usage; exit 0 }
        "--seconds" {
            if ($index + 1 -ge $args.Count) { Fail-Clip "--seconds needs a number" 2 }
            $SecondsLimit = $args[$index + 1]
            $index += 2
        }
        "--serial" {
            if ($index + 1 -ge $args.Count) { Fail-Clip "--serial needs a value" 2 }
            $Serial = [string]$args[$index + 1]
            $index += 2
        }
        "--out" {
            if ($index + 1 -ge $args.Count) { Fail-Clip "--out needs a directory" 2 }
            $OutParent = [string]$args[$index + 1]
            $index += 2
        }
        default { Fail-Clip "unknown argument: $arg" 2 }
    }
}

$secondsNumber = 0
if (-not [int]::TryParse([string]$SecondsLimit, [ref]$secondsNumber) -or $secondsNumber -lt 30 -or $secondsNumber -gt 60) {
    Fail-Clip "--seconds must be an integer from 30 to 60" 2
}
$SecondsLimit = $secondsNumber

if ($Serial -and $Serial -notmatch '^[A-Za-z0-9._:-]+$') {
    Fail-Clip "--serial has unexpected characters" 2
}

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
if (-not $OutParent) {
    $OutParent = Join-Path $root "recordings\quest-clips"
}
$stamp = [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
$destDir = Join-Path $OutParent $stamp
$destFile = Join-Path $destDir "ohh60-quest-teleop.mp4"
$serialDisplay = if ($Serial) { $Serial } else { "<serial>" }

function Show-Plan {
    Write-Output "OHH-60 record-quest-clip: dry-run. No adb call. No device. No robot command."
    Write-Output "Would check: adb devices -l"
    Write-Output "Would require one row whose status is device and whose model contains Quest."
    Write-Output "Would run: adb -s $serialDisplay shell rm -f $Remote"
    Write-Output "Would run: adb -s $serialDisplay shell screenrecord --size $Size --bit-rate $Bitrate --time-limit $SecondsLimit $Remote"
    Write-Output "Would run: adb -s $serialDisplay pull $Remote $destFile"
    Write-Output "Would run: adb -s $serialDisplay shell rm -f $Remote"
    Write-Output "Next: watch the mp4, rename it sim or real using docs/recording/quest-teleop-clip.md, and upload. This script never sends a robot command."
}

if ($DryRun) {
    Show-Plan
    exit 0
}

if (-not (Get-Command adb -ErrorAction SilentlyContinue)) {
    Fail-Clip "adb is not on PATH"
}

$listing = & adb devices -l
if ($LASTEXITCODE -ne 0) {
    Fail-Clip "adb devices failed"
}
$questSerials = New-Object System.Collections.Generic.List[string]
$skipped = New-Object System.Collections.Generic.List[string]
foreach ($line in $listing) {
    $text = ([string]$line).Trim()
    if (-not $text -or $text.StartsWith("List of devices attached")) { continue }
    $parts = $text -split '\s+'
    if ($parts.Count -lt 2) { continue }
    $rowSerial = $parts[0]
    $rowStatus = $parts[1]
    if ($rowStatus -ne "device") { continue }
    if ($Serial -and $rowSerial -ne $Serial) { continue }
    if ($text -match '(?i)model:\S*Quest') {
        $questSerials.Add($rowSerial)
        continue
    }
    $model = (& adb -s $rowSerial shell getprop ro.product.model | Out-String).Trim()
    if ($model -match '(?i)quest') {
        $questSerials.Add($rowSerial)
    } else {
        if (-not $model) { $model = "no model" }
        $skipped.Add("$rowSerial ($model)")
    }
}

if ($questSerials.Count -eq 0) {
    $listing | ForEach-Object { [Console]::Error.WriteLine($_) }
    if ($skipped.Count -gt 0) {
        [Console]::Error.WriteLine("Not a Quest:")
        $skipped | ForEach-Object { [Console]::Error.WriteLine($_) }
    }
    Fail-Clip "adb devices did not show a Quest in the device state. Allow the USB prompt on the headset if the row says unauthorized."
}
if ($questSerials.Count -gt 1) {
    $questSerials | ForEach-Object { [Console]::Error.WriteLine($_) }
    Fail-Clip "more than one Quest is attached. Re-run with --serial."
}
$chosen = $questSerials[0]

Write-Output "OHH-60 record-quest-clip: recording ${SecondsLimit}s from ${chosen}."
Write-Output "Perform the shot list in docs/recording/quest-teleop-clip.md. This script will not send a robot command."
& adb -s $chosen shell rm -f $Remote
if ($LASTEXITCODE -ne 0) {
    Write-Output "record-quest-clip: no leftover file at $Remote (or rm failed). Continuing."
}
& adb -s $chosen shell screenrecord --size $Size --bit-rate $Bitrate --time-limit $SecondsLimit $Remote
if ($LASTEXITCODE -ne 0) {
    Fail-Clip "screenrecord failed. If the headset rejects ${Size}, use Meta Quest Developer Hub. See docs/recording/quest-teleop-clip.md. This script did not send a robot command."
}
New-Item -ItemType Directory -Force -Path $destDir | Out-Null
& adb -s $chosen pull $Remote $destFile
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $destFile) -or (Get-Item $destFile).Length -le 0) {
    Fail-Clip "pull produced an empty file. The headset copy is still at $Remote."
}
& adb -s $chosen shell rm -f $Remote
if ($LASTEXITCODE -ne 0) {
    [Console]::Error.WriteLine("record-quest-clip: pulled the file; could not remove $Remote on the headset.")
}

Write-Output "Pulled: $destFile"
Write-Output "Next:"
Write-Output "1. Watch the file. If it is black or only the 2D shell, record again from Meta Quest Developer Hub. See docs/recording/quest-teleop-clip.md."
Write-Output "2. Rename to ohh60-sim-quest-<UTC date>.mp4 or ohh60-real-quest-<UTC date>.mp4. REAL only after the OHH-234 stages pass."
Write-Output "3. Pair it with the external room camera and upload with the title and description in that doc."
Write-Output "4. This script did not send a robot command."
