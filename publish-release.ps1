# Baut die öffentliche Release-EXE (ohne eingebettete Spielgrafiken) für GitHub und schreibt die Prüfsumme (SHA256SUMS).
# Version aus Directory.Build.props. Ausgabe: artifacts\<Version>\release. Ein vorhandener Ausgabeordner wird nie überschrieben.
# Danach auf GitHub ein Release mit Tag „v<Version>“ anlegen und BEIDE Dateien anhängen (EXE und SHA256SUMS).
param(
    [string]$Output
)

$ErrorActionPreference = 'Stop'
[xml]$props = Get-Content (Join-Path $PSScriptRoot 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw 'Keine <Version> in Directory.Build.props gefunden.' }
if (-not $Output) { $Output = Join-Path $PSScriptRoot "artifacts\$version\release" }
if (Test-Path $Output) { throw "Ausgabeordner existiert schon: $Output (wird nicht überschrieben)." }

$buildDir = Join-Path $env:TEMP ("Aion2DpsMeter-release-" + [Guid]::NewGuid().ToString('N'))
try {
    dotnet publish (Join-Path $PSScriptRoot 'src\Aion2DpsMeter.App') -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --artifacts-path $buildDir -o "$buildDir\out"
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish fehlgeschlagen ($LASTEXITCODE)." }

    New-Item -ItemType Directory -Path $Output | Out-Null
    $name = "Aion2DpsMeter-$version-win-x64.exe"
    Copy-Item "$buildDir\out\Aion2DpsMeter.App.exe" (Join-Path $Output $name)
    $hash = (Get-FileHash (Join-Path $Output $name) -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $Output 'SHA256SUMS'), "$hash  $name`n", [Text.UTF8Encoding]::new($false))
    Write-Host "Fertig: $Output"
    Write-Host "Tag: v$version  Dateien: $name, SHA256SUMS"
}
finally {
    if (Test-Path $buildDir) { Remove-Item -Recurse -Force $buildDir }
}
