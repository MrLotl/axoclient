# Veröffentlicht eine neue AxoClient-Version: erstellt den Tag und schiebt ihn zu GitHub.
# GitHub baut daraufhin die AxoClient.exe (siehe .github/workflows/release.yml).
#
# Aufruf:  .\release.ps1 0.2.0-alpha.1
param([Parameter(Mandatory)][string]$Version)

# Git schreibt auch normale Hinweise ("Everything up-to-date") in die Fehlerausgabe; deshalb nicht bei jeder
# Ausgabe abbrechen, sondern den Rückgabewert der Git-Befehle prüfen.
$ErrorActionPreference = 'Continue'

function Fail([string]$message) {
    Write-Host $message -ForegroundColor Red
    exit 1
}

function Git {
    & git @args 2>&1 | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) {
        Fail "Befehl fehlgeschlagen: git $args"
    }
}

Set-Location $PSScriptRoot
$Version = $Version.TrimStart('v')
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$') {
    Fail "Ungültige Version '$Version'. Beispiele: 0.2.0, 0.2.0-alpha.1, 1.0.0-beta.2"
}
if (-not (Test-Path .git)) {
    Fail 'Dieser Ordner ist noch kein Git-Repository. Einmalig: git init -b main; git add -A; git commit -m "AxoClient"'
}
if (-not (git remote)) {
    Fail 'Es ist noch kein GitHub-Repository verbunden. Einmalig: git remote add origin https://github.com/NAME/axoclient.git; git push -u origin main'
}
if (git status --porcelain) {
    Fail 'Es gibt noch nicht committete Änderungen. Bitte zuerst committen (git add -A; git commit -m "...").'
}
if (git tag --list "v$Version") {
    Fail "Version v$Version gibt es schon."
}

Git push
Git tag "v$Version"
Git push origin "v$Version"
Write-Host "v$Version wurde hochgeladen. Den Build siehst du auf GitHub unter 'Actions', danach unter 'Releases'." -ForegroundColor Green
