param(
    [string]$Version = '0.1.0',
    [string]$RuntimeVersion = '8.0.33'
)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $releaseRoot = Join-Path $PSScriptRoot "artifacts/package-v$Version"
    if (Test-Path $releaseRoot) { throw "Output already exists: $releaseRoot. Use a fresh output version." }
    $publishPath = Join-Path $releaseRoot 'PersonalRecorder'
    dotnet publish src/PersonalRecorder/PersonalRecorder.csproj -c Release -p:Platform=x64 "-p:Version=$Version" "-p:RuntimeFrameworkVersion=$RuntimeVersion" --self-contained true -o $publishPath
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    $assets = Get-Content src/PersonalRecorder/obj/project.assets.json -Raw | ConvertFrom-Json
    $packageFolders = $assets.packageFolders.PSObject.Properties.Name
    $licensesPath = Join-Path $publishPath 'licenses'
    New-Item -ItemType Directory -Path $licensesPath | Out-Null
    foreach ($package in @('microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64')) {
        $packagePath = $packageFolders | ForEach-Object { Join-Path $_ "$package/$RuntimeVersion" } | Where-Object { Test-Path $_ } | Select-Object -First 1
        if (!$packagePath) { throw "Runtime package missing: $package" }
        $notices = Get-ChildItem $packagePath -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)' }
        if (!$notices) { throw "Runtime license missing: $package" }
        foreach ($notice in $notices) { Copy-Item $notice.FullName (Join-Path $licensesPath "$package-$($notice.Name)") }
    }
    $zipPath = Join-Path $releaseRoot "PersonalRecorder-v$Version-win-x64.zip"
    Compress-Archive -Path "$publishPath/*" -DestinationPath $zipPath -CompressionLevel Optimal
    $hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($zipPath))" | Set-Content (Join-Path $releaseRoot 'SHA256SUMS.txt') -Encoding ascii
    Write-Host "Release ready: $zipPath"
} finally { Pop-Location }
