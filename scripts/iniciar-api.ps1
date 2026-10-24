$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($env:ConnectionStrings__Transferencias)) {
    $env:ConnectionStrings__Transferencias = [Environment]::GetEnvironmentVariable('ConnectionStrings__Transferencias', [EnvironmentVariableTarget]::User)
}
if ([string]::IsNullOrWhiteSpace($env:ConnectionStrings__Transferencias)) {
    throw 'Configura ConnectionStrings__Transferencias para BD_Equipo2. No guardes la contraseña en el repositorio.'
}
# La conexión se reutiliza sin imprimir sus credenciales. El adaptador valida la base antes de usarla.
& dotnet run --project (Join-Path $PSScriptRoot '../src/Transferencias.Api') --launch-profile http
if ($LASTEXITCODE -ne 0) { throw 'La API no pudo iniciarse. Revisa la configuración local.' }
