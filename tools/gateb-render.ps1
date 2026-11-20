<#
.SYNOPSIS
  Gate B: render the reconstructed pair against the original and pixel-diff them.

.DESCRIPTION
  For each source <name>.fml (+ sibling .gam), export it, rebuild the pair with
  `MfmeFmlDecoder --rebuild`, render both with mfme_pw.ps1 and diff with
  diff_png.ps1 (-Threshold 16 -IgnoreTop 48). MFME is single-instance, so renders
  are strictly sequential.

  Needs mfme_pw.ps1 and diff_png.ps1 (default: %TEMP%\opencode).
#>
param(
  [string]$FmlRoot = 'G:\DifLegacySection(fmlout-samples)',
  [string[]]$Names = @(),
  [string]$Exe = 'D:\projects\Active\MonoProjects\mfme-fml-decoder\bin\Release\net8.0\MfmeFmlDecoder.exe',
  [string]$PwScript = (Join-Path $env:TEMP 'opencode\mfme_pw.ps1'),
  [string]$DiffScript = (Join-Path $env:TEMP 'opencode\diff_png.ps1'),
  [string]$Work = (Join-Path $env:TEMP 'opencode\gateB'),
  [int]$WaitSeconds = 35,
  [int]$Threshold = 16,
  [int]$IgnoreTop = 48
)
$ErrorActionPreference = 'Continue'
if ($Names.Count -eq 0) {
  $Names = Get-ChildItem $FmlRoot -Filter *.fml | ForEach-Object { $_.BaseName }
}
Remove-Item $Work -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $Work | Out-Null

$report = @()
foreach ($name in $Names) {
  $fml = Join-Path $FmlRoot ($name + '.fml')
  $gam = Join-Path $FmlRoot ($name + '.gam')
  if (-not (Test-Path -LiteralPath $fml)) { $report += [pscustomobject]@{ Name = $name; Status = 'SRC-MISSING' }; continue }

  $d = Join-Path $Work $name
  $bd = Join-Path $d 'before'; $ad = Join-Path $d 'after'
  New-Item -ItemType Directory -Force -Path $bd, $ad | Out-Null
  Copy-Item -LiteralPath $fml (Join-Path $bd ($name + '.fml')) -Force
  if (Test-Path -LiteralPath $gam) { Copy-Item -LiteralPath $gam (Join-Path $bd ($name + '.gam')) -Force }

  $exp = Join-Path $d ($name + '.zip')
  & $Exe (Join-Path $bd ($name + '.fml')) "--export=$exp" *>$null
  if (-not (Test-Path -LiteralPath $exp)) { $report += [pscustomobject]@{ Name = $name; Status = 'EXPORT-FAIL' }; continue }

  & $Exe "--rebuild=$exp" "--outdir=$ad" *>$null
  $afml = Join-Path $ad ($name + '.fml')
  if (-not (Test-Path -LiteralPath $afml)) { $report += [pscustomobject]@{ Name = $name; Status = 'REBUILD-FAIL' }; continue }

  $bp = Join-Path $d 'before.png'; $ap = Join-Path $d 'after.png'
  $bo = & $PwScript -Fml (Join-Path $bd ($name + '.fml')) -OutPng $bp -WaitSeconds $WaitSeconds 2>&1 | Out-String
  $bok = $bo -match 'PW ok'
  $ao = & $PwScript -Fml $afml -OutPng $ap -WaitSeconds $WaitSeconds 2>&1 | Out-String
  $aok = $ao -match 'PW ok'

  if (-not $bok -and -not $aok) { $report += [pscustomobject]@{ Name = $name; Status = 'LOAD-FAIL-BOTH'; Detail = (($bo + $ao) -replace '\s+', ' ').Trim() }; continue }
  if (-not $bok) { $report += [pscustomobject]@{ Name = $name; Status = 'LOAD-FAIL-ORIGINAL'; Detail = ($bo -replace '\s+', ' ').Trim() }; continue }
  if (-not $aok) { $report += [pscustomobject]@{ Name = $name; Status = 'LOAD-FAIL-REBUILT'; Detail = ($ao -replace '\s+', ' ').Trim() }; continue }

  $dr = & $DiffScript -Before $bp -After $ap -Threshold $Threshold -IgnoreTop $IgnoreTop 2>&1 | Out-String
  $pct = -1.0
  if ($dr -match '\(([\d.]+)%\)') { $pct = [double]$matches[1] }
  $status = if ($pct -eq 0) { 'identical' } else { 'diff' }
  $report += [pscustomobject]@{ Name = $name; Status = $status; DiffPct = $pct; Detail = ($dr -replace '\s+', ' ').Trim() }
  Write-Output ("{0}: {1}" -f $name, $status)
}

$report | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $Work 'report.json')
$report | Format-Table -AutoSize
