# Baut die Variante „persoenlich“: wie die Endnutzer-EXE, aber mit den Klassenbildern aus dem eigenen Icon-Ordner
# (Dokumente\Aion2DpsMeter\Icons) eingebettet. Nur für den eigenen Gebrauch – enthält NCSoft-Grafiken, nicht weitergeben.
# Die Bilder werden nicht ins Projekt kopiert; Zwischendateien landen in einem eigenen Ordner unter %TEMP%.
# Ein vorhandener Ausgabeordner wird nie überschrieben.
param(
    [string]$Output = (Join-Path $PSScriptRoot 'artifacts\persoenlich-win-x64'),
    [string]$IconDir = ''
)

$ErrorActionPreference = 'Stop'
if (Test-Path $Output) {
    Write-Error "Ausgabeordner existiert schon, wird nicht überschrieben: $Output (anderen Namen mit -Output angeben)"
}

$buildDir = Join-Path $env:TEMP ("Aion2DpsMeter-persoenlich-" + [Guid]::NewGuid().ToString('N'))
$arguments = @(
    'publish', (Join-Path $PSScriptRoot 'src\Aion2DpsMeter.App'),
    '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
    '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EmbedPersonalIcons=true',
    '--artifacts-path', $buildDir,
    '-o', $Output
)
if ($IconDir) { $arguments += "-p:PersonalIconDir=$IconDir" }

try {
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { Write-Error "Bauen fehlgeschlagen (Exit-Code $LASTEXITCODE)." }
}
finally {
    if (Test-Path $buildDir) { Remove-Item -Recurse -Force $buildDir }
}

$note = @'
PERSÖNLICHE VARIANTE – NICHT WEITERGEBEN

Diese EXE enthält Klassen-Grafiken von AION 2 (Eigentum von NCSoft), die der Besitzer für den eigenen Gebrauch
aus einem Screenshot ausgeschnitten hat. Sie darf nicht weitergegeben oder veröffentlicht werden.
Zum Weitergeben die Endnutzer-EXE (ohne fremde Grafiken) verwenden.
'@
Set-Content -Path (Join-Path $Output 'NICHT-WEITERGEBEN.txt') -Value $note -Encoding utf8

# Patchnotes dieser Version aus CHANGELOG.md (wie im Release-Ordner).
[xml]$props = Get-Content (Join-Path $PSScriptRoot 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
$section = @(); $inside = $false
foreach ($line in Get-Content (Join-Path $PSScriptRoot 'CHANGELOG.md') -Encoding utf8) {
    if ($line -match '^##\s+v?(\d+\.\d+\.\d+)') { $inside = ($Matches[1] -eq $version); continue }
    if ($inside -and $line.StartsWith('- ')) { $section += $line }
}
[IO.File]::WriteAllText((Join-Path $Output 'PATCHNOTES.txt'), (($section -join "`n") + "`n"), [Text.UTF8Encoding]::new($false))
Write-Host "Fertig: $Output"
