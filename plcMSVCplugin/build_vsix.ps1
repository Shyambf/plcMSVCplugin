$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptDir

Write-Host "Building Planning C Visual Studio Extension..." -ForegroundColor Cyan
dotnet build "$ScriptDir\plcMSVCplugin.csproj" -c Release

$vsixRelease = Join-Path $ScriptDir "bin\Release\net472\PlanningC.vsix"
$vsixRoot = Join-Path $ScriptDir "..\PlanningC.vsix"

if (Test-Path $vsixRelease) {
    Copy-Item $vsixRelease $vsixRoot -Force
    Write-Host "`n[SUCCESS] VSIX package built successfully: $vsixRoot" -ForegroundColor Green
    Write-Host "To install in Visual Studio, run:" -ForegroundColor Yellow
    Write-Host "  Start-Process '$vsixRoot'" -ForegroundColor White
} else {
    Write-Host "`n[ERROR] Failed to find generated VSIX package." -ForegroundColor Red
}
