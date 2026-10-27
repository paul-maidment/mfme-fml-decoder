# Verify that a Native AOT decoder produces the same JSON as the checked-in goldens.
#
# This catches trimming/reflection regressions that the JIT test suite cannot see:
# the AOT binary must match every layout golden and every GameConfig golden.
#
# Usage:
#   ./scripts/verify-aot-json.ps1 -DecoderPath dist/aot/win-x64/MfmeFmlDecoder.exe
#   ./scripts/verify-aot-json.ps1 -DecoderPath dist/aot/linux-x64/MfmeFmlDecoder -RepoRoot .

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $DecoderPath,

    [string] $RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

$RepoRoot = (Resolve-Path -LiteralPath $RepoRoot).Path
$DecoderPath = (Resolve-Path -LiteralPath $DecoderPath).Path

function Assert-JsonTextMatches {
    param(
        [string] $ExpectedPath,
        [string] $ActualJson,
        [string] $Label
    )

    $expected = [System.Text.Json.Nodes.JsonNode]::Parse([System.IO.File]::ReadAllText($ExpectedPath))
    $actual = [System.Text.Json.Nodes.JsonNode]::Parse($ActualJson)
    if (-not [System.Text.Json.Nodes.JsonNode]::DeepEquals($expected, $actual)) {
        throw "JSON mismatch: $Label"
    }
}

function Assert-JsonFilesMatch {
    param(
        [string] $ExpectedPath,
        [string] $ActualPath,
        [string] $Label
    )

    Assert-JsonTextMatches -ExpectedPath $ExpectedPath -ActualJson ([System.IO.File]::ReadAllText($ActualPath)) -Label $Label
}

function Invoke-Decoder {
    param([string[]] $Arguments)

    $stdoutPath = [System.IO.Path]::GetTempFileName()
    $stderrPath = [System.IO.Path]::GetTempFileName()
    try {
        & $DecoderPath @Arguments 1> $stdoutPath 2> $stderrPath
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0) {
            $stderr = [System.IO.File]::ReadAllText($stderrPath).Trim()
            throw "decoder exited $exitCode. $stderr"
        }

        return [System.IO.File]::ReadAllText($stdoutPath)
    }
    finally {
        Remove-Item -LiteralPath $stdoutPath, $stderrPath -Force -ErrorAction SilentlyContinue
    }
}

$failures = [System.Collections.Generic.List[string]]::new()

$layoutPassed = 0
$layoutTotal = 0
$smokeRoot = Join-Path $RepoRoot 'test/smoke_test'
foreach ($expectedPath in Get-ChildItem -LiteralPath $smokeRoot -Recurse -Filter 'expected.json') {
    $caseDir = $expectedPath.Directory
    $layoutPath = Join-Path $caseDir.FullName 'layout.fml'
    if (-not (Test-Path -LiteralPath $layoutPath)) {
        continue
    }

    $layoutTotal++
    $caseName = [System.IO.Path]::GetRelativePath($smokeRoot, $caseDir.FullName)
    try {
        $actualJson = Invoke-Decoder -Arguments @($layoutPath, '--json')
        Assert-JsonTextMatches -ExpectedPath $expectedPath.FullName -ActualJson $actualJson -Label "layout $caseName"
        $layoutPassed++
    }
    catch {
        $failures.Add("layout $caseName : $($_.Exception.Message)")
    }
}

$gameConfigPassed = 0
$gameConfigTotal = 0
$gameConfigRoot = Join-Path $RepoRoot 'test/gameconfig_smoke'
if (Test-Path -LiteralPath $gameConfigRoot) {
    foreach ($caseDir in Get-ChildItem -LiteralPath $gameConfigRoot -Directory) {
        $layoutPath = Join-Path $caseDir.FullName 'layout.fml'
        $gamPath = Join-Path $caseDir.FullName 'layout.gam'
        $expectedMachinePath = Join-Path $caseDir.FullName 'expected.machine.json'
        $expectedGamePath = Join-Path $caseDir.FullName 'expected.game.json'
        if (-not (Test-Path -LiteralPath $layoutPath) -or
            -not (Test-Path -LiteralPath $gamPath) -or
            -not (Test-Path -LiteralPath $expectedMachinePath) -or
            -not (Test-Path -LiteralPath $expectedGamePath)) {
            continue
        }

        $gameConfigTotal++
        $zipPath = Join-Path ([System.IO.Path]::GetTempPath()) ("mfme-aot-verify-" + [guid]::NewGuid().ToString('N') + '.zip')
        $extractDir = Join-Path ([System.IO.Path]::GetTempPath()) ("mfme-aot-verify-" + [guid]::NewGuid().ToString('N'))
        try {
            Invoke-Decoder -Arguments @($layoutPath, "--export=$zipPath") | Out-Null
            if (-not (Test-Path -LiteralPath $zipPath)) {
                throw 'export did not create a zip'
            }

            Expand-Archive -LiteralPath $zipPath -DestinationPath $extractDir -Force
            Assert-JsonFilesMatch -ExpectedPath $expectedMachinePath -ActualPath (Join-Path $extractDir 'machine.json') -Label "gameconfig $($caseDir.Name) machine.json"
            Assert-JsonFilesMatch -ExpectedPath $expectedGamePath -ActualPath (Join-Path $extractDir 'game.json') -Label "gameconfig $($caseDir.Name) game.json"
            $gameConfigPassed++
        }
        catch {
            $failures.Add("gameconfig $($caseDir.Name) : $($_.Exception.Message)")
        }
        finally {
            Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $extractDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Host "AOT layouts: $layoutPassed/$layoutTotal match"
Write-Host "AOT GameConfig: $gameConfigPassed/$gameConfigTotal match"

if ($failures.Count -gt 0) {
    Write-Host ''
    Write-Host 'AOT verification failed:' -ForegroundColor Red
    foreach ($failure in $failures) {
        Write-Host "  $failure" -ForegroundColor Red
    }

    exit 1
}
