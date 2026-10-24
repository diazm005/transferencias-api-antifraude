# Transferencias API + antifraude

Proyecto educativo en **.NET 10**: API de transferencias con Clean Architecture,
Entity Framework, SQL Server y JWT, integrada con un evaluador de fraude independiente.
Esta copia es **autocontenida**: no requiere el repositorio del taller anterior.

| Servicio | Puerto | Función |
|---|---:|---|
| `Transferencias.Api` | 5307 | Recibe solicitudes autenticadas, ejecuta casos de uso y conserva el seguimiento en SQL. |
| `Fraude.Batch` | 5310 | Recibe JSON y devuelve riesgo, motivo y versión de regla. También acepta una entrada por consola con `--once`. |

```text
Cliente → API → Application → interfaces de Domain
                               ↑ implementadas por infraestructura
                               ├── EF → BD_Equipo2
                               └── HTTP → Fraude.Batch
```

El evaluador no referencia las capas de transferencias ni accede a la BD. Aplicación
no conoce la URL del evaluador ni los detalles de EF. Cambiar una integración consiste
en reemplazar el adaptador que implementa el contrato existente.

## Clonar y comprobar

Requisitos: SDK .NET 10 estable; PowerShell 7 para los scripts `.ps1`.
La API necesita acceso a la red del SQL Server del equipo. Las pruebas predeterminadas
no necesitan SQL, credenciales ni claves JWT locales.

```powershell
git clone https://github.com/diazm005/transferencias-api-antifraude.git
cd transferencias-api-antifraude
./scripts/validar.ps1
```

Abre **Transferencias.slnx** en Visual Studio compatible con .NET 10, o usa la CLI.
Los dos servicios son ejecutables separados; los otros proyectos son capas y pruebas.

## Configuración local

La conexión se configura con `ConnectionStrings__Transferencias` en tu entorno local.
Para configurar solo la terminal actual sin escribir la contraseña en el historial:

```powershell
$conexionSegura = Read-Host 'Cadena de conexión a BD_Equipo2' -AsSecureString
$env:ConnectionStrings__Transferencias = [System.Net.NetworkCredential]::new('', $conexionSegura).Password
```

Formato orientativo, reemplazando los marcadores con los datos del equipo:

```text
Server=<servidor>,<puerto>;Database=BD_Equipo2;User ID=<usuario>;Password=<contraseña>;Encrypt=True;TrustServerCertificate=False
```

El script de arranque también puede leer la variable del usuario de Windows si ya la
tienes configurada. En el servidor del taller con certificado no confiable puede ser
necesario `TrustServerCertificate=True` para esa conexión de desarrollo. La API solo
admite **BD_Equipo2**; no ejecuta migraciones, crea tablas ni carga datos al arrancar.

Genera un token de desarrollo para un cliente existente:

```powershell
$token = ./scripts/nuevo-token.ps1 -Dni 12345678
```

La clave queda en User Secrets, fuera de Git. El token dura 15 minutos. Cada desarrollador
genera su clave y tokens en su equipo; la contraseña de SQL nunca se usa para autenticar clientes.

## Iniciar

En una terminal:

```powershell
./scripts/iniciar-batch.ps1
```

En otra terminal con la conexión configurada:

```powershell
./scripts/iniciar-api.ps1
```

Swagger: <http://127.0.0.1:5307/swagger>. Pulsa **Authorize** y pega únicamente el token.
Importa [la colección Postman](contratos/Taller.postman_collection.json) si prefieres
Postman y configura `accessToken` localmente, sin compartirlo.

| Método y ruta de la API | Función |
|---|---|
| GET `/api/client/{nationalId}` | Consulta del cliente autenticado. |
| GET `/api/account/{accountNumber}` | Consulta de una cuenta propia. |
| POST `/api/transactions` | Solicitud, confirmación o reintento de transferencia. |
| GET `/api/transactions/{transactionReference}` | Consulta del seguimiento persistido. |
| GET `/health/live` | Disponibilidad del proceso, sin autenticación. |

Las lecturas exigen `transferencias.read`; el envío exige `transferencias.write`.
No se permite operar cuentas ajenas. Si el batchero cae, el intento queda registrado
como error y no se debita. El reintento de una operación completada no repite el débito.

## Contenido

```text
src/
  Transferencias.Api/           Presentación, JWT, Swagger
  Application/                 Casos de uso y autorización por titular
  Domain/                      Entidades, reglas e interfaces
  Transferencias.Infrastructure/ Selección de adaptadores
  SqlServerInfrastructure/     EF, repositorios y consumo HTTP
  Fraude.Batch/                Evaluador independiente
tests/                        Negocio, EF, HTTP y JWT
scripts/                      Arranque, token y validación
database/                     Scripts de estructura y diagramas; no contiene la BD
contratos/                    Entrada/salida y colección Postman
docs/                         Guía y resultados de validación
```

La **base de datos permanece en el servidor compartido**. Este repositorio incluye
DDL, comentarios, diagramas y datos ficticios de demostración; no contiene respaldos,
archivos de SQL Server, datos privados ni credenciales. No vuelvas a crear el modelo
si ya está disponible en el servidor del equipo.

## Pruebas y alcance

`./scripts/validar.ps1` compila y ejecuta negocio, mapeo EF, cliente HTTP, JWT, API y
batch CLI sin acceder a SQL. GitHub Actions ejecuta esa misma validación.

La integración SQL es opcional y explícita:

```powershell
# Solo consulta el esquema y los repositorios.
dotnet run --project tests/Transferencias.Infrastructure.Tests -- --sql-read-only

# Crea datos ficticios propios, prueba el flujo y elimina únicamente esas filas.
dotnet run --project tests/Taller.Tests -- --sql-integration
```

La API y el batch HTTP están limitados a `Development`. JWT usa un emisor local de
desarrollo; OTP y cámara aún son simulados. Siguen pendientes identidad/login real,
autenticación entre servicios, conciliación y envío efectivo de notificaciones.

Más información: [guía del taller](docs/GUIA-DEL-TALLER.md), [contrato de fraude](contratos/fraude.md),
[modelo ER y diccionario](database/README.md), [validación de esta copia](docs/VALIDACION.md).
