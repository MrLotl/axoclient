# Baut die AxoMod für alle unterstützten Minecraft-Versionen, legt die Jars in
# src/AxoClient.Core/Resources/axomod ab und schreibt dort axomod-versions.json (mit SHA-256).
# Der Launcher bettet beides ein und legt die passende Jar beim Spielstart in die Instanz.
#
# Aufruf:  .\build-all.ps1            alle Versionen
#          .\build-all.ps1 1.21.8     nur diese (Manifest wird trotzdem aus allen vorhandenen Jars erzeugt)
param([string[]] $Only)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$target = Join-Path (Split-Path $PSScriptRoot) 'src\AxoClient.Core\Resources\axomod'
New-Item -ItemType Directory -Force $target | Out-Null
$modVersion = (Get-Content gradle.properties | Select-String '^mod_version=(.*)$').Matches[0].Groups[1].Value

function Read-Settings($version) {
    $settings = @{}
    foreach ($line in Get-Content "versions\$version.properties") {
        if ($line -match '^\s*([^#=]+?)\s*=\s*(.*?)\s*$') { $settings[$Matches[1]] = $Matches[2] }
    }
    $settings
}

$versions = Get-ChildItem versions -Filter *.properties | ForEach-Object { $_.BaseName } |
    Sort-Object { [version]($_ -replace '^(\d+\.\d+)$', '$1.0') }
foreach ($version in $versions) {
    if ($Only -and $Only -notcontains $version) { continue }
    Write-Host "== Minecraft $version" -ForegroundColor Cyan
    & .\gradlew.bat build "-Pmc=$version" --console=plain -q
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build für $version fehlgeschlagen." -ForegroundColor Red
        exit 1
    }
    Copy-Item "build\libs\axoclient-$modVersion+$version.jar" (Join-Path $target "axoclient-$version.jar") -Force
}

$entries = foreach ($version in $versions) {
    $jar = Join-Path $target "axoclient-$version.jar"
    if (-not (Test-Path $jar)) { continue }
    $settings = Read-Settings $version
    [ordered]@{
        minecraft = @($settings['minecraft'] -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
        loaders   = @('fabric')
        file      = "axoclient-$version.jar"
        level     = 'full'
        sha256    = (Get-FileHash $jar -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}
$manifest = [ordered]@{ formatVersion = 1; entries = @($entries) }
$json = $manifest | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText((Join-Path $target 'axomod-versions.json'), $json + "`n", (New-Object Text.UTF8Encoding $false))
Write-Host "Fertig: $(@($entries).Count) Jar(s) in $target" -ForegroundColor Green
