# Veröffentlicht eine neue AxoClient-Version: erstellt den Tag und schiebt ihn zu GitHub.
# GitHub baut daraufhin die AxoClient.exe (siehe .github/workflows/release.yml).
#
# Aufruf:  .\release.ps1 0.2.0-alpha.1
param([Parameter(Mandatory)][string]$Version)

$ErrorActionPreference = 'Stop'
$Version = $Version.TrimStart('v')
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$') {
    throw "Ungültige Version '$Version'. Beispiele: 0.2.0, 0.2.0-alpha.1, 1.0.0-beta.2"
}
Set-Location $PSScriptRoot
if (-not (Test-Path .git)) {
    throw 'Dieser Ordner ist noch kein Git-Repository. Einmalig: git init -b main; git add -A; git commit -m "AxoClient"'
}
if (-not (git remote)) {
    throw 'Es ist noch kein GitHub-Repository verbunden. Einmalig: git remote add origin https://github.com/NAME/axoclient.git; git push -u origin main'
}
if (git status --porcelain) {
    throw 'Es gibt noch nicht committete Änderungen. Bitte zuerst committen (git add -A; git commit -m "...").'
}
if (git tag --list "v$Version") {
    throw "Version v$Version gibt es schon."
}

git push
git tag "v$Version"
git push origin "v$Version"
Write-Host "v$Version wurde hochgeladen. Den Build siehst du auf GitHub unter 'Actions', danach unter 'Releases'."
