$ErrorActionPreference = 'Stop'
& dotnet run --project (Join-Path $PSScriptRoot '../src/Fraude.Batch') --launch-profile http
if ($LASTEXITCODE -ne 0) { throw 'El evaluador no pudo iniciarse.' }
