# Baut die AxoClient-Mod für alle unterstützten Minecraft-Versionen und legt sie in Assets/mods ab.
# Von dort nimmt der Launcher sie und kopiert sie beim Spielstart in Fabric-Instanzen.
#
# Aufruf:  .\build-all.ps1
$ErrorActionPreference = 'Continue'
Set-Location $PSScriptRoot

$target = Join-Path (Split-Path $PSScriptRoot) 'Assets\mods'
New-Item -ItemType Directory -Force $target | Out-Null

$versions = Get-ChildItem versions -Filter *.properties | ForEach-Object { $_.BaseName }
foreach ($version in $versions) {
    Write-Host "== Minecraft $version" -ForegroundColor Cyan
    & .\gradlew.bat build "-Pmc=$version" --console=plain -q
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build für $version fehlgeschlagen." -ForegroundColor Red
        exit 1
    }
    $modVersion = (Get-Content gradle.properties | Select-String '^mod_version=(.*)$').Matches[0].Groups[1].Value
    Copy-Item "build\libs\mclauncher-badge-$modVersion+$version.jar" (Join-Path $target "mclauncher-badge-$version.jar") -Force
}
Write-Host "Fertig: $($versions.Count) Version(en) in Assets\mods" -ForegroundColor Green
