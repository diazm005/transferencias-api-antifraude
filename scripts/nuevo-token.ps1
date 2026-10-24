param(
    [ValidatePattern('^[0-9]{8}$')][string]$Dni = '12345678',
    [switch]$SoloLectura
)
$ErrorActionPreference = 'Stop'
$proyecto = Join-Path $PSScriptRoot '../src/Transferencias.Api/Transferencias.Api.csproj'
# Clave propia de la nueva API, guardada en User Secrets. No utiliza las credenciales de SQL.
$argumentos = @('user-jwts', 'create', '--project', $proyecto, '--name', "cliente-$Dni",
    '--issuer', 'taller-transferencias-dev', '--audience', 'taller-transferencias-api',
    '--claim', "national_id=$Dni", '--scope', 'transferencias.read', '--valid-for', '15m', '--output', 'token')
if (-not $SoloLectura) { $argumentos += @('--scope', 'transferencias.write') }
& dotnet @argumentos
if ($LASTEXITCODE -ne 0) { throw 'No se pudo generar el token del taller.' }
