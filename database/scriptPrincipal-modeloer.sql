/*
SCRIPT PRINCIPAL DEL MODELO ER
Proyecto: Clean Transferencia
Base de datos: BD_Equipo2 | Esquema: transferencias

Orden de lectura
  1. Catálogos: bancos, segmentos, estados y niveles de riesgo.
  2. Entidades: clientes y cuentas.
  3. Operación: transferencia.
  4. Seguimiento: auditoría, movimientos, fraude y notificaciones.
  5. Índices de consulta.

Claves: PK identifica; FK relaciona; UQ evita duplicados; CK valida datos.
Este archivo conserva las 11 tablas, sus relaciones y restricciones.
Los datos ficticios y las pruebas están en scripts independientes.
La base ya fue creada: este archivo se puede estudiar sin ejecutarlo.

Se omitieron las 95 llamadas repetitivas a sp_addextendedproperty para
facilitar la lectura. Las descripciones ya guardadas en SQL Server permanecen
intactas; la versión completa está en database/001_crear_modelo.sql.
*/

SET NOCOUNT ON;
SET XACT_ABORT ON;

-- Protección: solo se permite crear objetos en la base del equipo 2.
IF DB_NAME() <> N'BD_Equipo2'
    THROW 51000, N'Operación detenida: ejecutar únicamente en BD_Equipo2.', 1;

-- No sustituir un modelo existente por accidente.
IF EXISTS (
    SELECT 1
    FROM sys.objects
    WHERE schema_id = SCHEMA_ID(N'transferencias')
      AND is_ms_shipped = 0
)
    THROW 51001, N'El esquema transferencias ya contiene objetos. No se reemplazará el modelo existente.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    IF SCHEMA_ID(N'transferencias') IS NULL
        EXEC(N'CREATE SCHEMA transferencias AUTHORIZATION dbo;');

    -- ==========================================================================
    -- 1. CATALOGOS
    -- ==========================================================================

    -- Identifica el banco de cada cuenta y el banco receptor. Permite distinguir una transferencia
    -- interna de una interbancaria sin repetir nombres.
    CREATE TABLE transferencias.Banco
    (
        BancoId                      int IDENTITY(1,1) NOT NULL,
        Codigo                       varchar(20) NOT NULL,
        Nombre                       nvarchar(100) NOT NULL,
        EsBancoPropio                bit NOT NULL
            CONSTRAINT DF_Banco_EsBancoPropio DEFAULT (0),
        Activo                       bit NOT NULL
            CONSTRAINT DF_Banco_Activo DEFAULT (1),

        CONSTRAINT PK_Banco PRIMARY KEY (BancoId),

        CONSTRAINT UQ_Banco_Codigo UNIQUE (Codigo),

        CONSTRAINT CK_Banco_Codigo CHECK (LEN(LTRIM(RTRIM(Codigo))) > 0),
        CONSTRAINT CK_Banco_Nombre CHECK (LEN(LTRIM(RTRIM(Nombre))) > 0)
    );

    -- Mantiene el límite diario por segmento que necesita ISegmentoLimiteRepository. Centraliza los
    -- valores Regular y Preferente.
    CREATE TABLE transferencias.Segmento
    (
        SegmentoId                   int IDENTITY(1,1) NOT NULL,
        Nombre                       nvarchar(50) NOT NULL,
        LimiteDiario                 decimal(18,2) NOT NULL,
        Activo                       bit NOT NULL
            CONSTRAINT DF_Segmento_Activo DEFAULT (1),

        CONSTRAINT PK_Segmento PRIMARY KEY (SegmentoId),

        CONSTRAINT UQ_Segmento_Nombre UNIQUE (Nombre),

        CONSTRAINT CK_Segmento_Limite CHECK (LimiteDiario >= 0),
        CONSTRAINT CK_Segmento_Nombre CHECK (LEN(LTRIM(RTRIM(Nombre))) > 0)
    );

    -- Catálogo que hace legibles los estados de la transferencia y conserva la correspondencia
    -- numérica con el enum actual del dominio.
    CREATE TABLE transferencias.EstadoTransferencia
    (
        EstadoTransferenciaId        tinyint NOT NULL,
        Nombre                       varchar(30) NOT NULL,
        Descripcion                  nvarchar(250) NOT NULL,

        CONSTRAINT PK_EstadoTransferencia PRIMARY KEY (EstadoTransferenciaId),

        CONSTRAINT UQ_EstadoTransferencia_Nombre UNIQUE (Nombre)
    );

    -- Catálogo de la respuesta de antifraude. Mantiene separados los resultados de riesgo y los
    -- estados financieros de una transferencia.
    CREATE TABLE transferencias.NivelRiesgo
    (
        NivelRiesgoId                tinyint NOT NULL,
        Nombre                       varchar(20) NOT NULL,
        Descripcion                  nvarchar(250) NOT NULL,

        CONSTRAINT PK_NivelRiesgo PRIMARY KEY (NivelRiesgoId),

        CONSTRAINT UQ_NivelRiesgo_Nombre UNIQUE (Nombre)
    );

    -- ==========================================================================
    -- 2. CLIENTES Y CUENTAS
    -- ==========================================================================

    -- Representa la entidad Cliente del dominio y relaciona la identidad de negocio con sus cuentas y
    -- su segmento.
    CREATE TABLE transferencias.Cliente
    (
        ClienteId                    bigint IDENTITY(1,1) NOT NULL,
        Dni                          varchar(8) NOT NULL,
        Nombres                      nvarchar(150) NOT NULL,
        SegmentoId                   int NOT NULL,
        Activo                       bit NOT NULL
            CONSTRAINT DF_Cliente_Activo DEFAULT (1),
        FechaCreacion                datetimeoffset(3) NOT NULL
            CONSTRAINT DF_Cliente_Fecha DEFAULT (TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')),

        CONSTRAINT PK_Cliente PRIMARY KEY (ClienteId),

        CONSTRAINT UQ_Cliente_Dni UNIQUE (Dni),

        CONSTRAINT FK_Cliente_Segmento FOREIGN KEY (SegmentoId)
            REFERENCES transferencias.Segmento (SegmentoId),

        CONSTRAINT CK_Cliente_Dni CHECK (LEN(Dni) = 8 AND Dni COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9]%'),
        CONSTRAINT CK_Cliente_Nombres CHECK (LEN(LTRIM(RTRIM(Nombres))) > 0)
    );

    -- Representa las cuentas que administra el core simulado. Conserva el saldo disponible y un token
    -- de concurrencia para detectar actualizaciones simultáneas.
    CREATE TABLE transferencias.Cuenta
    (
        CuentaId                     bigint IDENTITY(1,1) NOT NULL,
        ClienteId                    bigint NOT NULL,
        BancoId                      int NOT NULL,
        -- Texto para conservar ceros iniciales, como 001.
        NumeroCuenta                 varchar(30) NOT NULL,
        Activa                       bit NOT NULL
            CONSTRAINT DF_Cuenta_Activa DEFAULT (1),
        -- Alcance actual: soles (PEN).
        Moneda                       char(3) NOT NULL
            CONSTRAINT DF_Cuenta_Moneda DEFAULT ('PEN'),
        -- Actualizar junto con los movimientos dentro de una misma transacción.
        SaldoDisponible              decimal(18,2) NOT NULL
            CONSTRAINT DF_Cuenta_Saldo DEFAULT (0),
        -- Token de concurrencia para EF; no es una fecha.
        VersionFila                  rowversion NOT NULL,
        FechaCreacion                datetimeoffset(3) NOT NULL
            CONSTRAINT DF_Cuenta_Fecha DEFAULT (TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')),

        CONSTRAINT PK_Cuenta PRIMARY KEY (CuentaId),

        CONSTRAINT UQ_Cuenta_BancoNumero UNIQUE (BancoId, NumeroCuenta),
        CONSTRAINT UQ_Cuenta_IdCliente UNIQUE (CuentaId, ClienteId),
        CONSTRAINT UQ_Cuenta_IdBancoNumero UNIQUE (CuentaId, BancoId, NumeroCuenta),

        CONSTRAINT FK_Cuenta_Cliente FOREIGN KEY (ClienteId)
            REFERENCES transferencias.Cliente (ClienteId),
        CONSTRAINT FK_Cuenta_Banco FOREIGN KEY (BancoId)
            REFERENCES transferencias.Banco (BancoId),

        CONSTRAINT CK_Cuenta_Numero CHECK (LEN(LTRIM(RTRIM(NumeroCuenta))) > 0),
        CONSTRAINT CK_Cuenta_Saldo CHECK (SaldoDisponible >= 0),
        CONSTRAINT CK_Cuenta_Moneda CHECK (Moneda = 'PEN')
    );

    -- ==========================================================================
    -- 3. TRANSFERENCIA
    -- ==========================================================================

    -- Almacena cada solicitud y su último estado. El GUID recibido como TransactionReference es único
    -- y permite reconocer reintentos de la misma operación.
    CREATE TABLE transferencias.Transferencia
    (
        -- GUID recibido como TransactionReference; se conserva en los reintentos.
        TransferenciaId              uniqueidentifier NOT NULL,
        ClienteId                    bigint NOT NULL,
        -- La FK compuesta también comprueba que el cliente sea titular.
        CuentaOrigenId               bigint NOT NULL,
        BancoDestinoId               int NOT NULL,
        -- El destino externo no necesita existir en nuestra tabla Cuenta.
        NumeroCuentaDestino          varchar(30) NOT NULL,
        -- Solo se informa si el destino es una cuenta local.
        CuentaDestinoInternaId       bigint NULL,
        Monto                        decimal(18,2) NOT NULL,
        -- Valor aplicado a esta operación; conserva el costo histórico.
        Comision                     decimal(18,2) NOT NULL
            CONSTRAINT DF_Transferencia_Comision DEFAULT (0),
        -- Campo calculado: monto más comisión.
        TotalDebito                  AS (Monto + Comision) PERSISTED,
        Moneda                       char(3) NOT NULL
            CONSTRAINT DF_Transferencia_Moneda DEFAULT ('PEN'),
        -- Se conserva el instante mediante datetimeoffset; valor inicial en UTC.
        FechaSolicitud               datetimeoffset(3) NOT NULL
            CONSTRAINT DF_Transferencia_Fecha DEFAULT (TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')),
        -- Campo calculado: fecha de Lima (UTC-5) para el límite diario.
        FechaNegocio                 AS (CONVERT(date, SWITCHOFFSET(FechaSolicitud, '-05:00'))) PERSISTED,
        EstadoTransferenciaId        tinyint NOT NULL,
        Mensaje                      nvarchar(500) NOT NULL
            CONSTRAINT DF_Transferencia_Mensaje DEFAULT (N''),
        Notificada                   bit NOT NULL
            CONSTRAINT DF_Transferencia_Notificada DEFAULT (0),
        -- Detecta actualizaciones simultaneas del estado.
        VersionFila                  rowversion NOT NULL,

        CONSTRAINT PK_Transferencia PRIMARY KEY (TransferenciaId),

        CONSTRAINT FK_Transferencia_Cliente FOREIGN KEY (ClienteId)
            REFERENCES transferencias.Cliente (ClienteId),
        CONSTRAINT FK_Transferencia_OrigenTitular FOREIGN KEY (CuentaOrigenId, ClienteId)
            REFERENCES transferencias.Cuenta (CuentaId, ClienteId),
        CONSTRAINT FK_Transferencia_BancoDestino FOREIGN KEY (BancoDestinoId)
            REFERENCES transferencias.Banco (BancoId),
        CONSTRAINT FK_Transferencia_DestinoInterno FOREIGN KEY (CuentaDestinoInternaId, BancoDestinoId, NumeroCuentaDestino)
            REFERENCES transferencias.Cuenta (CuentaId, BancoId, NumeroCuenta),
        CONSTRAINT FK_Transferencia_Estado FOREIGN KEY (EstadoTransferenciaId)
            REFERENCES transferencias.EstadoTransferencia (EstadoTransferenciaId),

        CONSTRAINT CK_Transferencia_Id CHECK (TransferenciaId <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_Transferencia_Monto CHECK (Monto > 0),
        CONSTRAINT CK_Transferencia_Comision CHECK (Comision >= 0),
        CONSTRAINT CK_Transferencia_Moneda CHECK (Moneda = 'PEN'),
        CONSTRAINT CK_Transferencia_Destino CHECK (LEN(LTRIM(RTRIM(NumeroCuentaDestino))) > 0),
        CONSTRAINT CK_Transferencia_CuentasDiferentes CHECK (CuentaDestinoInternaId IS NULL OR CuentaDestinoInternaId <> CuentaOrigenId)
    );

    -- ==========================================================================
    -- 4. SEGUIMIENTO DE LA OPERACION
    -- ==========================================================================

    -- Conserva el historial de estados sin depender solo de la última fila de Transferencia. Permite
    -- explicar rechazos, confirmaciones y resultados inciertos.
    CREATE TABLE transferencias.AuditoriaTransferencia
    (
        AuditoriaTransferenciaId     bigint IDENTITY(1,1) NOT NULL,
        TransferenciaId              uniqueidentifier NOT NULL,
        EstadoTransferenciaId        tinyint NOT NULL,
        FechaEvento                  datetimeoffset(3) NOT NULL
            CONSTRAINT DF_Auditoria_Fecha DEFAULT (TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')),
        Mensaje                      nvarchar(500) NOT NULL,
        TraceIdentifier              nvarchar(100) NULL,
        Origen                       nvarchar(100) NOT NULL
            CONSTRAINT DF_Auditoria_Origen DEFAULT (N'Application'),

        CONSTRAINT PK_AuditoriaTransferencia PRIMARY KEY (AuditoriaTransferenciaId),

        CONSTRAINT FK_Auditoria_Transferencia FOREIGN KEY (TransferenciaId)
            REFERENCES transferencias.Transferencia (TransferenciaId),
        CONSTRAINT FK_Auditoria_Estado FOREIGN KEY (EstadoTransferenciaId)
            REFERENCES transferencias.EstadoTransferencia (EstadoTransferenciaId),

        CONSTRAINT UQ_Auditoria_IdOperacion UNIQUE (AuditoriaTransferenciaId, TransferenciaId)
    );

    -- Registra cada efecto sobre un saldo y separa monto, comisión y crédito interno. La unicidad
    -- impide registrar dos veces el mismo concepto para una cuenta y operación.
    CREATE TABLE transferencias.MovimientoCuenta
    (
        MovimientoCuentaId           bigint IDENTITY(1,1) NOT NULL,
        TransferenciaId              uniqueidentifier NOT NULL,
        CuentaId                     bigint NOT NULL,
        -- Separa monto, comisión, crédito al destino y reversos confirmados.
        TipoMovimiento               varchar(20) NOT NULL,
        -- D disminuye el saldo; C lo incrementa.
        Naturaleza                   char(1) NOT NULL,
        -- Importe positivo. Una comisión cero no genera movimiento.
        Monto                        decimal(18,2) NOT NULL,
        SaldoAnterior                decimal(18,2) NOT NULL,
        SaldoPosterior               decimal(18,2) NOT NULL,
        FechaMovimiento              datetimeoffset(3) NOT NULL
            CONSTRAINT DF_Movimiento_Fecha DEFAULT (TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')),

        CONSTRAINT PK_MovimientoCuenta PRIMARY KEY (MovimientoCuentaId),

        CONSTRAINT FK_Movimiento_Transferencia FOREIGN KEY (TransferenciaId)
            REFERENCES transferencias.Transferencia (TransferenciaId),
        CONSTRAINT FK_Movimiento_Cuenta FOREIGN KEY (CuentaId)
            REFERENCES transferencias.Cuenta (CuentaId),

        CONSTRAINT UQ_Movimiento_OperacionCuentaTipo UNIQUE (TransferenciaId, CuentaId, TipoMovimiento),

        CONSTRAINT CK_Movimiento_Monto CHECK (Monto > 0),
        CONSTRAINT CK_Movimiento_Saldos CHECK (SaldoAnterior >= 0 AND SaldoPosterior >= 0),
        CONSTRAINT CK_Movimiento_Tipo CHECK ((TipoMovimiento IN ('DEBITO_MONTO','DEBITO_COMISION','REVERSO_DESTINO') AND Naturaleza = 'D') OR (TipoMovimiento IN ('CREDITO_DESTINO','REVERSO_MONTO','REVERSO_COMISION') AND Naturaleza = 'C')),
        CONSTRAINT CK_Movimiento_Calculo CHECK (SaldoPosterior = CASE WHEN Naturaleza = 'D' THEN SaldoAnterior - Monto ELSE SaldoAnterior + Monto END)
    );

    -- Prepara la persistencia de solicitudes y respuestas del futuro evaluador o batch. La falta de
    -- respuesta se distingue de un resultado de riesgo Bajo.
    CREATE TABLE transferencias.EvaluacionFraude
    (
        EvaluacionFraudeId           bigint IDENTITY(1,1) NOT NULL,
        TransferenciaId              uniqueidentifier NOT NULL,
        NumeroIntento                int NOT NULL
            CONSTRAINT DF_Fraude_Intento DEFAULT (1),
        -- Estado del evaluador; independiente del estado financiero.
        EstadoEvaluacion             varchar(12) NOT NULL
            CONSTRAINT DF_Fraude_Estado DEFAULT ('Pendiente'),
        -- NULL hasta que exista una respuesta válida del evaluador.
        NivelRiesgoId                tinyint NULL,
        Motivo                       nvarchar(500) NULL,
        -- Identifica las reglas con las que se tomó la decisión.
        VersionRegla                 varchar(50) NOT NULL,
        Proveedor                    nvarchar(100) NOT NULL,
        FechaSolicitud               datetimeoffset(3) NOT NULL
            CONSTRAINT DF_Fraude_Fecha DEFAULT (TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')),
        FechaRespuesta               datetimeoffset(3) NULL,
        VersionFila                  rowversion NOT NULL,

        CONSTRAINT PK_EvaluacionFraude PRIMARY KEY (EvaluacionFraudeId),

        CONSTRAINT FK_Fraude_Transferencia FOREIGN KEY (TransferenciaId)
            REFERENCES transferencias.Transferencia (TransferenciaId),
        CONSTRAINT FK_Fraude_Nivel FOREIGN KEY (NivelRiesgoId)
            REFERENCES transferencias.NivelRiesgo (NivelRiesgoId),

        CONSTRAINT UQ_Fraude_OperacionIntento UNIQUE (TransferenciaId, NumeroIntento),

        CONSTRAINT CK_Fraude_Intento CHECK (NumeroIntento > 0),
        CONSTRAINT CK_Fraude_Estado CHECK (EstadoEvaluacion IN ('Pendiente','Procesando','Completada','Error')),
        CONSTRAINT CK_Fraude_Resultado CHECK ((EstadoEvaluacion IN ('Pendiente','Procesando') AND NivelRiesgoId IS NULL AND FechaRespuesta IS NULL) OR (EstadoEvaluacion = 'Completada' AND NivelRiesgoId IS NOT NULL AND FechaRespuesta IS NOT NULL) OR (EstadoEvaluacion = 'Error' AND NivelRiesgoId IS NULL AND FechaRespuesta IS NOT NULL)),
        CONSTRAINT CK_Fraude_Fechas CHECK (FechaRespuesta IS NULL OR FechaRespuesta >= FechaSolicitud)
    );

    -- Conserva los avisos por entregar para permitir reintentos sin cambiar el resultado financiero.
    -- Prepara el patrón de mensajes pendientes; el worker se implementará después.
    CREATE TABLE transferencias.NotificacionCliente
    (
        NotificacionClienteId        bigint IDENTITY(1,1) NOT NULL,
        TransferenciaId              uniqueidentifier NOT NULL,
        -- El aviso corresponde a un evento de esta misma transferencia.
        AuditoriaTransferenciaId     bigint NOT NULL,
        Canal                        varchar(10) NOT NULL,
        EstadoEnvio                  varchar(10) NOT NULL
            CONSTRAINT DF_Notificacion_Estado DEFAULT ('Pendiente'),
        Intentos                     int NOT NULL
            CONSTRAINT DF_Notificacion_Intentos DEFAULT (0),
        FechaCreacion                datetimeoffset(3) NOT NULL
            CONSTRAINT DF_Notificacion_Fecha DEFAULT (TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')),
        -- Aceptación por el proveedor; no confirma lectura del cliente.
        FechaEnvio                   datetimeoffset(3) NULL,
        ProximoIntento               datetimeoffset(3) NULL,
        UltimoError                  nvarchar(500) NULL,
        VersionFila                  rowversion NOT NULL,

        CONSTRAINT PK_NotificacionCliente PRIMARY KEY (NotificacionClienteId),

        CONSTRAINT FK_Notificacion_Transferencia FOREIGN KEY (TransferenciaId)
            REFERENCES transferencias.Transferencia (TransferenciaId),
        CONSTRAINT FK_Notificacion_Auditoria FOREIGN KEY (AuditoriaTransferenciaId, TransferenciaId)
            REFERENCES transferencias.AuditoriaTransferencia (AuditoriaTransferenciaId, TransferenciaId),

        CONSTRAINT UQ_Notificacion_EventoCanal UNIQUE (AuditoriaTransferenciaId, Canal),

        CONSTRAINT CK_Notificacion_Canal CHECK (Canal IN ('SMS','Correo')),
        CONSTRAINT CK_Notificacion_Estado CHECK (EstadoEnvio IN ('Pendiente','Enviada','Error')),
        CONSTRAINT CK_Notificacion_Intentos CHECK (Intentos >= 0),
        CONSTRAINT CK_Notificacion_Fecha CHECK ((EstadoEnvio = 'Enviada' AND FechaEnvio IS NOT NULL AND Intentos > 0) OR (EstadoEnvio IN ('Pendiente','Error') AND FechaEnvio IS NULL)),
        CONSTRAINT CK_Notificacion_FechaOrden CHECK (FechaEnvio IS NULL OR FechaEnvio >= FechaCreacion)
    );

    -- ==========================================================================
    -- 5. INDICES DE CONSULTA
    -- ==========================================================================

    -- Banco
    CREATE UNIQUE INDEX UX_Banco_Propio
        ON transferencias.Banco (EsBancoPropio) WHERE EsBancoPropio = 1;

    -- Cliente
    CREATE INDEX IX_Cliente_Segmento
        ON transferencias.Cliente (SegmentoId);

    -- Cuenta
    CREATE INDEX IX_Cuenta_Cliente
        ON transferencias.Cuenta (ClienteId);

    -- Transferencia
    CREATE INDEX IX_Transferencia_ClienteDia
        ON transferencias.Transferencia (ClienteId, FechaNegocio) INCLUDE (Monto, EstadoTransferenciaId, CuentaOrigenId);
    CREATE INDEX IX_Transferencia_OrigenFecha
        ON transferencias.Transferencia (CuentaOrigenId, FechaSolicitud);
    CREATE INDEX IX_Transferencia_Pendientes
        ON transferencias.Transferencia (EstadoTransferenciaId, FechaSolicitud) WHERE EstadoTransferenciaId = 3;

    -- AuditoriaTransferencia
    CREATE INDEX IX_Auditoria_TransferenciaFecha
        ON transferencias.AuditoriaTransferencia (TransferenciaId, FechaEvento, AuditoriaTransferenciaId);

    -- MovimientoCuenta
    CREATE INDEX IX_Movimiento_CuentaFecha
        ON transferencias.MovimientoCuenta (CuentaId, FechaMovimiento);

    -- EvaluacionFraude
    CREATE INDEX IX_Fraude_Pendientes
        ON transferencias.EvaluacionFraude (EstadoEvaluacion, FechaSolicitud) INCLUDE (TransferenciaId);

    -- NotificacionCliente
    CREATE INDEX IX_Notificacion_Pendientes
        ON transferencias.NotificacionCliente (EstadoEnvio, ProximoIntento) INCLUDE (TransferenciaId, Intentos);

    COMMIT TRANSACTION;
END TRY
BEGIN CATCH
    -- Si la creación falla, se revierten los objetos creados en esta transacción.
    IF @@TRANCOUNT > 0
        ROLLBACK TRANSACTION;
    THROW;
END CATCH;
