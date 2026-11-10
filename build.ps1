$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet publish src/PersonalRecorder/PersonalRecorder.csproj -c Release -p:Platform=x64 --self-contained false -o artifacts/PersonalRecorder
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Write-Host 'Ready: artifacts/PersonalRecorder/PersonalRecorder.exe'
} finally { Pop-Location }
