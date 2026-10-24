$ErrorActionPreference = 'Stop'
$raiz = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $raiz
try {
    & dotnet build Transferencias.slnx -m:1 --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación.' }
    foreach ($proyecto in @('tests/Transferencias.Tests', 'tests/Transferencias.Infrastructure.Tests', 'tests/Taller.Tests')) {
        & dotnet run --project $proyecto --no-build
        if ($LASTEXITCODE -ne 0) { throw "Fallaron las comprobaciones de $proyecto." }
    }
    # La entrada del archivo es ficticia. Este modo nunca accede a SQL ni inicia un servidor.
    $salida = Get-Content contratos/entrada-fraude.json -Raw | & dotnet src/Fraude.Batch/bin/Debug/net10.0/Fraude.Batch.dll --once
    if ($LASTEXITCODE -ne 0 -or ($salida | ConvertFrom-Json).riskLevel -ne 'Medio') { throw 'Falló el contrato CLI del batch.' }
    $salidaInvalida = '{"amount":-1}' | & dotnet src/Fraude.Batch/bin/Debug/net10.0/Fraude.Batch.dll --once
    if ($LASTEXITCODE -ne 2 -or -not ($salidaInvalida | ConvertFrom-Json).errors) { throw 'El batch debe rechazar la entrada inválida.' }
    'Validación terminada. Ninguna prueba abrió una conexión SQL.'
}
finally { Pop-Location }
exit 0
