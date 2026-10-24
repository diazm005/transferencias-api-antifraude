# Comprobaciones de infraestructura

Ejecutar desde la raíz del repositorio:

```powershell
dotnet run --project tests/Transferencias.Infrastructure.Tests
```

El ejecutable construye el modelo de Entity Framework con un servidor ficticio y compara sus metadatos contra `database/001_crear_modelo.sql`. Comprueba tablas, columnas, tipos, claves compuestas, índices, columnas calculadas, concurrencia y conservación del historial. También verifica el guardado exclusivo en `BD_Equipo2` y los bordes de la política de riesgo del taller.

Estas comprobaciones **no abren conexiones, no ejecutan scripts y no modifican bases de datos**. No requieren credenciales. El script se copia a la carpeta de salida solamente para leerlo como contrato del modelo existente.

El resultado valida el mapeo, la configuración y las reglas puras. Las transacciones, los bloqueos entre instancias, los saldos simultáneos y el procesamiento real del batch necesitan pruebas de integración separadas con SQL Server.

El modo offline también realiza 16 comprobaciones del cliente HTTP con un transporte
simulado: contrato enviado, niveles válidos, respuestas inválidas, HTTP 503 y cancelación.

## Validación real mediante consultas de lectura

Con `ConnectionStrings__Transferencias` configurada en la sesión:

```powershell
dotnet run --project tests/Transferencias.Infrastructure.Tests -- --sql-read-only
```

Esta opción abre una conexión exclusivamente a `BD_Equipo2`. Verifica el nombre de la
base antes de consultar el esquema, compara columnas/tipos/nulabilidad/identidades,
columnas calculadas y relaciones con EF, y lee las once entidades y consultas de los
repositorios. Comprueba los valores de los catálogos de estados y riesgo. No imprime
credenciales ni filas de clientes. No ejecuta DDL, INSERT, UPDATE, DELETE ni el batch.

Validación del 23/10/2026: conexión y lecturas correctas; el mapeo coincide con las
11 tablas, 84 columnas y 16 relaciones desplegadas en BD_Equipo2. Esto no verifica
todavía escritura, rollback financiero, concurrencia ni procesamiento de la cola.

## Batchero HTTP y seguimiento SQL con rollback

Con el batchero iniciado y `ConnectionStrings__Transferencias` configurada:

```powershell
dotnet run --project tests/Transferencias.Infrastructure.Tests -- --sql-http-rollback
```

Esta prueba sí escribe filas temporales de transferencia, auditoría y evaluación en
BD_Equipo2 dentro de una transacción que siempre revierte. No llama al débito ni a la
cámara. Comprueba una respuesta HTTP real registrada como Completada, un HTTP 503
simulado registrado como Error y ausencia de movimientos. Después del rollback
verifica que sus filas no permanecen. La identidad de las tablas puede avanzar aun
con rollback. La URL se configura mediante `Antifraude__BaseUrl` (por defecto 127.0.0.1:5310).

Resultado verificado: cuatro comprobaciones correctas, sin filas de prueba persistentes.
