/*
Modelo ER inicial del taller Clean Transferencia.
Destino exclusivo: BD_Equipo2, esquema transferencias.
Fecha: 2026-10-17. Todas las cantidades están expresadas en soles (PEN).
No crea ni modifica otras bases; no elimina ni reemplaza objetos existentes.
Ejecutar una sola vez. Si el esquema ya tiene objetos, se detiene para revisión.
El modelo prepara EF, fraude y notificaciones; no implementa esos procesos.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF DB_NAME() <> N'BD_Equipo2'
    THROW 51000, N'Operación detenida: ejecutar únicamente en BD_Equipo2.', 1;
IF EXISTS (SELECT 1 FROM sys.objects WHERE schema_id = SCHEMA_ID(N'transferencias') AND is_ms_shipped = 0)
    THROW 51001, N'El esquema transferencias ya contiene objetos. No se reemplazará el modelo existente.', 1;

BEGIN TRY
    BEGIN TRANSACTION;
    IF SCHEMA_ID(N'transferencias') IS NULL
        EXEC(N'CREATE SCHEMA transferencias AUTHORIZATION dbo;');

    -- Banco: Identifica el banco de cada cuenta y el banco receptor. Permite distinguir una transferencia interna de una interbancaria sin repetir nombres.
    CREATE TABLE transferencias.Banco
    (
        -- Clave interna del banco.
        BancoId int IDENTITY(1,1) NOT NULL,
        -- Código estable utilizado por la API; por ejemplo BANCO_A.
        Codigo varchar(20) NOT NULL,
        -- Nombre descriptivo del banco.
        Nombre nvarchar(100) NOT NULL,
        -- Indica el banco local del taller; solo uno puede estar marcado.
        EsBancoPropio bit NOT NULL CONSTRAINT DF_Banco_EsBancoPropio DEFAULT (0),
        -- Permite deshabilitar un banco sin borrar su historial.
        Activo bit NOT NULL CONSTRAINT DF_Banco_Activo DEFAULT (1),
        CONSTRAINT PK_Banco PRIMARY KEY (BancoId),
        CONSTRAINT UQ_Banco_Codigo UNIQUE (Codigo),
        CONSTRAINT CK_Banco_Codigo CHECK (LEN(LTRIM(RTRIM(Codigo))) > 0),
        CONSTRAINT CK_Banco_Nombre CHECK (LEN(LTRIM(RTRIM(Nombre))) > 0)
    );

    CREATE UNIQUE INDEX UX_Banco_Propio ON transferencias.Banco (EsBancoPropio) WHERE EsBancoPropio = 1;

    -- Segmento: Mantiene el límite diario por segmento que necesita ISegmentoLimiteRepository. Centraliza los valores Regular y Preferente.
    CREATE TABLE transferencias.Segmento
    (
        -- Clave interna del segmento.
        SegmentoId int IDENTITY(1,1) NOT NULL,
        -- Nombre que corresponde a Cliente.Segmento en el dominio.
        Nombre nvarchar(50) NOT NULL,
        -- Monto principal máximo por cliente y día de Lima, sin incluir comisiones.
        LimiteDiario decimal(18,2) NOT NULL,
        -- Permite deshabilitar un segmento sin borrar clientes.
        Activo bit NOT NULL CONSTRAINT DF_Segmento_Activo DEFAULT (1),
        CONSTRAINT PK_Segmento PRIMARY KEY (SegmentoId),
        CONSTRAINT UQ_Segmento_Nombre UNIQUE (Nombre),
        CONSTRAINT CK_Segmento_Limite CHECK (LimiteDiario >= 0),
        CONSTRAINT CK_Segmento_Nombre CHECK (LEN(LTRIM(RTRIM(Nombre))) > 0)
    );


    -- Cliente: Representa la entidad Cliente del dominio y relaciona la identidad de negocio con sus cuentas y su segmento.
    CREATE TABLE transferencias.Cliente
    (
        -- Clave interna del cliente; independiente de su documento.
        ClienteId bigint IDENTITY(1,1) NOT NULL,
        -- Documento de ocho dígitos y clave de búsqueda del contrato actual. No es una contraseña.
        Dni varchar(8) NOT NULL,
        -- Nombre completo usado por la entidad Cliente.
        Nombres nvarchar(150) NOT NULL,
        -- Segmento que determina el límite diario.
        SegmentoId int NOT NULL,
        -- Estado administrativo; el adaptador EF deberá considerar este campo al consultar.
        Activo bit NOT NULL CONSTRAINT DF_Cliente_Activo DEFAULT (1),
        -- Fecha de creación expresada en UTC.
        FechaCreacion datetimeoffset(3) NOT NULL CONSTRAINT DF_Cliente_Fecha DEFAULT (TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')),
        CONSTRAINT PK_Cliente PRIMARY KEY (ClienteId),
        CONSTRAINT UQ_Cliente_Dni UNIQUE (Dni),
        CONSTRAINT FK_Cliente_Segmento FOREIGN KEY (SegmentoId) REFERENCES transferencias.Segmento (SegmentoId),
        CONSTRAINT CK_Cliente_Dni CHECK (LEN(Dni) = 8 AND Dni COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9]%'),
        CONSTRAINT CK_Cliente_Nombres CHECK (LEN(LTRIM(RTRIM(Nombres))) > 0)
    );

    CREATE INDEX IX_Cliente_Segmento ON transferencias.Cliente (SegmentoId);

    -- Cuenta: Representa las cuentas que administra el core simulado. Conserva el saldo disponible y un token de concurrencia para detectar actualizaciones simultáneas.
    CREATE TABLE transferencias.Cuenta
    (
        -- Clave interna de la cuenta.
        CuentaId bigint IDENTITY(1,1) NOT NULL,
        -- Titular de la cuenta.
        ClienteId bigint NOT NULL,
        -- Banco al que pertenece. En este taller se registran aquí las cuentas locales.
        BancoId int NOT NULL,
        -- Número tratado como texto para conservar ceros iniciales, como 001.
        NumeroCuenta varchar(30) NOT NULL,
        -- Corresponde a Cuenta.Activa.
        Activa bit NOT NULL CONSTRAINT DF_Cuenta_Activa DEFAULT (1),
        -- Alcance inicial: únicamente soles. El modelo no realiza conversión de moneda.
        Moneda char(3) NOT NULL CONSTRAINT DF_Cuenta_Moneda DEFAULT ('PEN'),
        -- Saldo actual. Debe actualizarse en la misma transacción que registra el movimiento.
        SaldoDisponible decimal(18,2) NOT NULL CONSTRAINT DF_Cuenta_Saldo DEFAULT (0),
        -- Token binario de concurrencia para EF; no representa una fecha.
        VersionFila rowversion NOT NULL,
        -- Fecha de creación en UTC.
        FechaCreacion datetimeoffset(3) NOT NULL CONSTRAINT DF_Cuenta_Fecha DEFAULT (TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')),
        CONSTRAINT PK_Cuenta PRIMARY KEY (CuentaId),
        CONSTRAINT UQ_Cuenta_BancoNumero UNIQUE (BancoId, NumeroCuenta),
        CONSTRAINT UQ_Cuenta_IdCliente UNIQUE (CuentaId, ClienteId),
        CONSTRAINT UQ_Cuenta_IdBancoNumero UNIQUE (CuentaId, BancoId, NumeroCuenta),
        CONSTRAINT FK_Cuenta_Cliente FOREIGN KEY (ClienteId) REFERENCES transferencias.Cliente (ClienteId),
        CONSTRAINT FK_Cuenta_Banco FOREIGN KEY (BancoId) REFERENCES transferencias.Banco (BancoId),
        CONSTRAINT CK_Cuenta_Numero CHECK (LEN(LTRIM(RTRIM(NumeroCuenta))) > 0),
        CONSTRAINT CK_Cuenta_Saldo CHECK (SaldoDisponible >= 0),
        CONSTRAINT CK_Cuenta_Moneda CHECK (Moneda = 'PEN')
    );

    CREATE INDEX IX_Cuenta_Cliente ON transferencias.Cuenta (ClienteId);

    -- EstadoTransferencia: Catálogo que hace legibles los estados de la transferencia y conserva la correspondencia numérica con el enum actual del dominio.
    CREATE TABLE transferencias.EstadoTransferencia
    (
        -- Valor actual del enum: 0 Rechazada, 1 RequiereConfirmacion, 2 NoDisponible, 3 Pendiente, 4 Completada.
        EstadoTransferenciaId tinyint NOT NULL,
        -- Nombre del estado actual del dominio.
        Nombre varchar(30) NOT NULL,
        -- Explicación del estado para revisión del equipo.
        Descripcion nvarchar(250) NOT NULL,
        CONSTRAINT PK_EstadoTransferencia PRIMARY KEY (EstadoTransferenciaId),
        CONSTRAINT UQ_EstadoTransferencia_Nombre UNIQUE (Nombre)
    );


    -- NivelRiesgo: Catálogo de la respuesta de antifraude. Mantiene separados los resultados de riesgo y los estados financieros de una transferencia.
    CREATE TABLE transferencias.NivelRiesgo
    (
        -- Valor del enum actual: 0 Bajo, 1 Medio, 2 Alto.
        NivelRiesgoId tinyint NOT NULL,
        -- Nombre del nivel de riesgo.
        Nombre varchar(20) NOT NULL,
        -- Interpretación del resultado para el taller.
        Descripcion nvarchar(250) NOT NULL,
        CONSTRAINT PK_NivelRiesgo PRIMARY KEY (NivelRiesgoId),
        CONSTRAINT UQ_NivelRiesgo_Nombre UNIQUE (Nombre)
    );


    -- Transferencia: Almacena cada solicitud y su último estado. El GUID recibido como TransactionReference es único y permite reconocer reintentos de la misma operación.
    CREATE TABLE transferencias.Transferencia
    (
        -- ID enviado por el consumidor. Debe conservarse en reintentos; no generar otro al repetir.
        TransferenciaId uniqueidentifier NOT NULL,
        -- Cliente solicitante y titular obligatorio de la cuenta origen.
        ClienteId bigint NOT NULL,
        -- Cuenta local desde la que se solicita el débito.
        CuentaOrigenId bigint NOT NULL,
        -- Banco receptor.
        BancoDestinoId int NOT NULL,
        -- Identificación del destino externo o interno; no exige que exista una cuenta externa localmente.
        NumeroCuentaDestino varchar(30) NOT NULL,
        -- Referencia opcional cuando el destino está administrado por el core local. La FK comprueba banco y número.
        CuentaDestinoInternaId bigint NULL,
        -- Monto principal que recibe el destino.
        Monto decimal(18,2) NOT NULL,
        -- Comisión calculada por el dominio y conservada como valor histórico.
        Comision decimal(18,2) NOT NULL CONSTRAINT DF_Transferencia_Comision DEFAULT (0),
        -- Total solicitado al origen; campo calculado, no se envía desde EF al insertar.
        TotalDebito AS (Monto + Comision) PERSISTED,
        -- Moneda única del taller: PEN.
        Moneda char(3) NOT NULL CONSTRAINT DF_Transferencia_Moneda DEFAULT ('PEN'),
        -- Momento de la solicitud. Debe preservarse para conciliar y auditar.
        FechaSolicitud datetimeoffset(3) NOT NULL CONSTRAINT DF_Transferencia_Fecha DEFAULT (TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')),
        -- Día de Lima que agrupa el límite diario; calculado a partir de la solicitud.
        FechaNegocio AS (CONVERT(date, SWITCHOFFSET(FechaSolicitud, '-05:00'))) PERSISTED,
        -- Estado actual. Debe indicarse explícitamente al crear la operación.
        EstadoTransferenciaId tinyint NOT NULL,
        -- Motivo del resultado; no incluir credenciales ni OTP.
        Mensaje nvarchar(500) NOT NULL CONSTRAINT DF_Transferencia_Mensaje DEFAULT (N''),
        -- Compatibilidad con el dominio actual; los intentos de entrega se registran aparte.
        Notificada bit NOT NULL CONSTRAINT DF_Transferencia_Notificada DEFAULT (0),
        -- Token de concurrencia para impedir que EF sobrescriba cambios de estado sin detectarlos.
        VersionFila rowversion NOT NULL,
        CONSTRAINT PK_Transferencia PRIMARY KEY (TransferenciaId),
        CONSTRAINT FK_Transferencia_Cliente FOREIGN KEY (ClienteId) REFERENCES transferencias.Cliente (ClienteId),
        CONSTRAINT FK_Transferencia_OrigenTitular FOREIGN KEY (CuentaOrigenId, ClienteId) REFERENCES transferencias.Cuenta (CuentaId, ClienteId),
        CONSTRAINT FK_Transferencia_BancoDestino FOREIGN KEY (BancoDestinoId) REFERENCES transferencias.Banco (BancoId),
        CONSTRAINT FK_Transferencia_DestinoInterno FOREIGN KEY (CuentaDestinoInternaId, BancoDestinoId, NumeroCuentaDestino) REFERENCES transferencias.Cuenta (CuentaId, BancoId, NumeroCuenta),
        CONSTRAINT FK_Transferencia_Estado FOREIGN KEY (EstadoTransferenciaId) REFERENCES transferencias.EstadoTransferencia (EstadoTransferenciaId),
        CONSTRAINT CK_Transferencia_Id CHECK (TransferenciaId <> '00000000-0000-0000-0000-000000000000'),
        CONSTRAINT CK_Transferencia_Monto CHECK (Monto > 0),
        CONSTRAINT CK_Transferencia_Comision CHECK (Comision >= 0),
        CONSTRAINT CK_Transferencia_Moneda CHECK (Moneda = 'PEN'),
        CONSTRAINT CK_Transferencia_Destino CHECK (LEN(LTRIM(RTRIM(NumeroCuentaDestino))) > 0),
        CONSTRAINT CK_Transferencia_CuentasDiferentes CHECK (CuentaDestinoInternaId IS NULL OR CuentaDestinoInternaId <> CuentaOrigenId)
    );

    CREATE INDEX IX_Transferencia_ClienteDia ON transferencias.Transferencia (ClienteId, FechaNegocio) INCLUDE (Monto, EstadoTransferenciaId, CuentaOrigenId);
    CREATE INDEX IX_Transferencia_OrigenFecha ON transferencias.Transferencia (CuentaOrigenId, FechaSolicitud);
    CREATE INDEX IX_Transferencia_Pendientes ON transferencias.Transferencia (EstadoTransferenciaId, FechaSolicitud) WHERE EstadoTransferenciaId = 3;

    -- AuditoriaTransferencia: Conserva el historial de estados sin depender solo de la última fila de Transferencia. Permite explicar rechazos, confirmaciones y resultados inciertos.
    CREATE TABLE transferencias.AuditoriaTransferencia
    (
        -- Secuencia del evento de auditoría.
        AuditoriaTransferenciaId bigint IDENTITY(1,1) NOT NULL,
        -- Operación auditada.
        TransferenciaId uniqueidentifier NOT NULL,
        -- Estado registrado en este evento.
        EstadoTransferenciaId tinyint NOT NULL,
        -- Momento del evento en UTC.
        FechaEvento datetimeoffset(3) NOT NULL CONSTRAINT DF_Auditoria_Fecha DEFAULT (TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')),
        -- Descripción del evento y su motivo, sin datos secretos.
        Mensaje nvarchar(500) NOT NULL,
        -- Identificador de correlación de la solicitud; no contiene el token JWT.
        TraceIdentifier nvarchar(100) NULL,
        -- Componente o proceso que generó el evento.
        Origen nvarchar(100) NOT NULL CONSTRAINT DF_Auditoria_Origen DEFAULT (N'Application'),
        CONSTRAINT PK_AuditoriaTransferencia PRIMARY KEY (AuditoriaTransferenciaId),
        CONSTRAINT FK_Auditoria_Transferencia FOREIGN KEY (TransferenciaId) REFERENCES transferencias.Transferencia (TransferenciaId),
        CONSTRAINT FK_Auditoria_Estado FOREIGN KEY (EstadoTransferenciaId) REFERENCES transferencias.EstadoTransferencia (EstadoTransferenciaId),
        CONSTRAINT UQ_Auditoria_IdOperacion UNIQUE (AuditoriaTransferenciaId, TransferenciaId)
    );

    CREATE INDEX IX_Auditoria_TransferenciaFecha ON transferencias.AuditoriaTransferencia (TransferenciaId, FechaEvento, AuditoriaTransferenciaId);

    -- MovimientoCuenta: Registra cada efecto sobre un saldo y separa monto, comisión y crédito interno. La unicidad impide registrar dos veces el mismo concepto para una cuenta y operación.
    CREATE TABLE transferencias.MovimientoCuenta
    (
        -- Secuencia interna del movimiento.
        MovimientoCuentaId bigint IDENTITY(1,1) NOT NULL,
        -- Operación que origina el movimiento.
        TransferenciaId uniqueidentifier NOT NULL,
        -- Cuenta local afectada.
        CuentaId bigint NOT NULL,
        -- DEBITO_MONTO, DEBITO_COMISION, CREDITO_DESTINO o sus reversos. Un reverso necesita una decisión explícita de conciliación.
        TipoMovimiento varchar(20) NOT NULL,
        -- D disminuye el saldo; C lo incrementa.
        Naturaleza char(1) NOT NULL,
        -- Valor positivo del movimiento. Una comisión cero no genera movimiento.
        Monto decimal(18,2) NOT NULL,
        -- Saldo antes de aplicar este movimiento.
        SaldoAnterior decimal(18,2) NOT NULL,
        -- Saldo después del movimiento; el CHECK exige consistencia aritmética.
        SaldoPosterior decimal(18,2) NOT NULL,
        -- Momento de contabilización en UTC.
        FechaMovimiento datetimeoffset(3) NOT NULL CONSTRAINT DF_Movimiento_Fecha DEFAULT (TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')),
        CONSTRAINT PK_MovimientoCuenta PRIMARY KEY (MovimientoCuentaId),
        CONSTRAINT FK_Movimiento_Transferencia FOREIGN KEY (TransferenciaId) REFERENCES transferencias.Transferencia (TransferenciaId),
        CONSTRAINT FK_Movimiento_Cuenta FOREIGN KEY (CuentaId) REFERENCES transferencias.Cuenta (CuentaId),
        CONSTRAINT UQ_Movimiento_OperacionCuentaTipo UNIQUE (TransferenciaId, CuentaId, TipoMovimiento),
        CONSTRAINT CK_Movimiento_Monto CHECK (Monto > 0),
        CONSTRAINT CK_Movimiento_Saldos CHECK (SaldoAnterior >= 0 AND SaldoPosterior >= 0),
        CONSTRAINT CK_Movimiento_Tipo CHECK ((TipoMovimiento IN ('DEBITO_MONTO','DEBITO_COMISION','REVERSO_DESTINO') AND Naturaleza = 'D') OR (TipoMovimiento IN ('CREDITO_DESTINO','REVERSO_MONTO','REVERSO_COMISION') AND Naturaleza = 'C')),
        CONSTRAINT CK_Movimiento_Calculo CHECK (SaldoPosterior = CASE WHEN Naturaleza = 'D' THEN SaldoAnterior - Monto ELSE SaldoAnterior + Monto END)
    );

    CREATE INDEX IX_Movimiento_CuentaFecha ON transferencias.MovimientoCuenta (CuentaId, FechaMovimiento);

    -- EvaluacionFraude: Prepara la persistencia de solicitudes y respuestas del futuro evaluador o batch. La falta de respuesta se distingue de un resultado de riesgo Bajo.
    CREATE TABLE transferencias.EvaluacionFraude
    (
        -- Secuencia de evaluación.
        EvaluacionFraudeId bigint IDENTITY(1,1) NOT NULL,
        -- Operación que debe evaluarse antes del débito.
        TransferenciaId uniqueidentifier NOT NULL,
        -- Número de intento de evaluación dentro de la operación.
        NumeroIntento int NOT NULL CONSTRAINT DF_Fraude_Intento DEFAULT (1),
        -- Pendiente, Procesando, Completada o Error. Es independiente del estado financiero.
        EstadoEvaluacion varchar(12) NOT NULL CONSTRAINT DF_Fraude_Estado DEFAULT ('Pendiente'),
        -- Resultado del evaluador. Es obligatorio al completar y nulo mientras no exista un resultado.
        NivelRiesgoId tinyint NULL,
        -- Razón de la decisión o del error; no guardar datos de autenticación.
        Motivo nvarchar(500) NULL,
        -- Versión del conjunto de reglas utilizado; debe proporcionarla el evaluador.
        VersionRegla varchar(50) NOT NULL,
        -- Identifica al script, batch o servicio que realiza la evaluación.
        Proveedor nvarchar(100) NOT NULL,
        -- Fecha de creación de la solicitud de riesgo.
        FechaSolicitud datetimeoffset(3) NOT NULL CONSTRAINT DF_Fraude_Fecha DEFAULT (TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')),
        -- Momento en que terminó la evaluación o se registró su error.
        FechaRespuesta datetimeoffset(3) NULL,
        -- Token para coordinar cambios de estado entre consumidores.
        VersionFila rowversion NOT NULL,
        CONSTRAINT PK_EvaluacionFraude PRIMARY KEY (EvaluacionFraudeId),
        CONSTRAINT FK_Fraude_Transferencia FOREIGN KEY (TransferenciaId) REFERENCES transferencias.Transferencia (TransferenciaId),
        CONSTRAINT FK_Fraude_Nivel FOREIGN KEY (NivelRiesgoId) REFERENCES transferencias.NivelRiesgo (NivelRiesgoId),
        CONSTRAINT UQ_Fraude_OperacionIntento UNIQUE (TransferenciaId, NumeroIntento),
        CONSTRAINT CK_Fraude_Intento CHECK (NumeroIntento > 0),
        CONSTRAINT CK_Fraude_Estado CHECK (EstadoEvaluacion IN ('Pendiente','Procesando','Completada','Error')),
        CONSTRAINT CK_Fraude_Resultado CHECK ((EstadoEvaluacion IN ('Pendiente','Procesando') AND NivelRiesgoId IS NULL AND FechaRespuesta IS NULL) OR (EstadoEvaluacion = 'Completada' AND NivelRiesgoId IS NOT NULL AND FechaRespuesta IS NOT NULL) OR (EstadoEvaluacion = 'Error' AND NivelRiesgoId IS NULL AND FechaRespuesta IS NOT NULL)),
        CONSTRAINT CK_Fraude_Fechas CHECK (FechaRespuesta IS NULL OR FechaRespuesta >= FechaSolicitud)
    );

    CREATE INDEX IX_Fraude_Pendientes ON transferencias.EvaluacionFraude (EstadoEvaluacion, FechaSolicitud) INCLUDE (TransferenciaId);

    -- NotificacionCliente: Conserva los avisos por entregar para permitir reintentos sin cambiar el resultado financiero. Prepara el patrón de mensajes pendientes; el worker se implementará después.
    CREATE TABLE transferencias.NotificacionCliente
    (
        -- Identificador del aviso.
        NotificacionClienteId bigint IDENTITY(1,1) NOT NULL,
        -- Operación que se comunica.
        TransferenciaId uniqueidentifier NOT NULL,
        -- Evento preciso que originó el aviso.
        AuditoriaTransferenciaId bigint NOT NULL,
        -- SMS o Correo; cada evento puede generar un aviso por canal.
        Canal varchar(10) NOT NULL,
        -- Pendiente, Enviada o Error.
        EstadoEnvio varchar(10) NOT NULL CONSTRAINT DF_Notificacion_Estado DEFAULT ('Pendiente'),
        -- Número de intentos de entrega.
        Intentos int NOT NULL CONSTRAINT DF_Notificacion_Intentos DEFAULT (0),
        -- Momento de creación en UTC.
        FechaCreacion datetimeoffset(3) NOT NULL CONSTRAINT DF_Notificacion_Fecha DEFAULT (TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00')),
        -- Fecha de aceptación por el proveedor; no prueba lectura por el cliente.
        FechaEnvio datetimeoffset(3) NULL,
        -- Momento propuesto para reintentar.
        ProximoIntento datetimeoffset(3) NULL,
        -- Resumen del error de entrega sin secretos.
        UltimoError nvarchar(500) NULL,
        -- Token para coordinar reintentos entre consumidores.
        VersionFila rowversion NOT NULL,
        CONSTRAINT PK_NotificacionCliente PRIMARY KEY (NotificacionClienteId),
        CONSTRAINT FK_Notificacion_Transferencia FOREIGN KEY (TransferenciaId) REFERENCES transferencias.Transferencia (TransferenciaId),
        CONSTRAINT FK_Notificacion_Auditoria FOREIGN KEY (AuditoriaTransferenciaId, TransferenciaId) REFERENCES transferencias.AuditoriaTransferencia (AuditoriaTransferenciaId, TransferenciaId),
        CONSTRAINT UQ_Notificacion_EventoCanal UNIQUE (AuditoriaTransferenciaId, Canal),
        CONSTRAINT CK_Notificacion_Canal CHECK (Canal IN ('SMS','Correo')),
        CONSTRAINT CK_Notificacion_Estado CHECK (EstadoEnvio IN ('Pendiente','Enviada','Error')),
        CONSTRAINT CK_Notificacion_Intentos CHECK (Intentos >= 0),
        CONSTRAINT CK_Notificacion_Fecha CHECK ((EstadoEnvio = 'Enviada' AND FechaEnvio IS NOT NULL AND Intentos > 0) OR (EstadoEnvio IN ('Pendiente','Error') AND FechaEnvio IS NULL)),
        CONSTRAINT CK_Notificacion_FechaOrden CHECK (FechaEnvio IS NULL OR FechaEnvio >= FechaCreacion)
    );

    CREATE INDEX IX_Notificacion_Pendientes ON transferencias.NotificacionCliente (EstadoEnvio, ProximoIntento) INCLUDE (TransferenciaId, Intentos);

    -- Las descripciones se guardan como propiedades extendidas y son visibles en SSMS.
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Identifica el banco de cada cuenta y el banco receptor. Permite distinguir una transferencia interna de una interbancaria sin repetir nombres.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Banco';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Clave interna del banco.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Banco', @level2type=N'COLUMN', @level2name=N'BancoId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Código estable utilizado por la API; por ejemplo BANCO_A.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Banco', @level2type=N'COLUMN', @level2name=N'Codigo';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Nombre descriptivo del banco.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Banco', @level2type=N'COLUMN', @level2name=N'Nombre';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Indica el banco local del taller; solo uno puede estar marcado.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Banco', @level2type=N'COLUMN', @level2name=N'EsBancoPropio';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Permite deshabilitar un banco sin borrar su historial.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Banco', @level2type=N'COLUMN', @level2name=N'Activo';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Mantiene el límite diario por segmento que necesita ISegmentoLimiteRepository. Centraliza los valores Regular y Preferente.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Segmento';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Clave interna del segmento.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Segmento', @level2type=N'COLUMN', @level2name=N'SegmentoId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Nombre que corresponde a Cliente.Segmento en el dominio.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Segmento', @level2type=N'COLUMN', @level2name=N'Nombre';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Monto principal máximo por cliente y día de Lima, sin incluir comisiones.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Segmento', @level2type=N'COLUMN', @level2name=N'LimiteDiario';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Permite deshabilitar un segmento sin borrar clientes.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Segmento', @level2type=N'COLUMN', @level2name=N'Activo';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Representa la entidad Cliente del dominio y relaciona la identidad de negocio con sus cuentas y su segmento.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Cliente';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Clave interna del cliente; independiente de su documento.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Cliente', @level2type=N'COLUMN', @level2name=N'ClienteId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Documento de ocho dígitos y clave de búsqueda del contrato actual. No es una contraseña.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Cliente', @level2type=N'COLUMN', @level2name=N'Dni';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Nombre completo usado por la entidad Cliente.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Cliente', @level2type=N'COLUMN', @level2name=N'Nombres';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Segmento que determina el límite diario.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Cliente', @level2type=N'COLUMN', @level2name=N'SegmentoId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Estado administrativo; el adaptador EF deberá considerar este campo al consultar.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Cliente', @level2type=N'COLUMN', @level2name=N'Activo';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Fecha de creación expresada en UTC.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Cliente', @level2type=N'COLUMN', @level2name=N'FechaCreacion';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Representa las cuentas que administra el core simulado. Conserva el saldo disponible y un token de concurrencia para detectar actualizaciones simultáneas.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Cuenta';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Clave interna de la cuenta.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Cuenta', @level2type=N'COLUMN', @level2name=N'CuentaId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Titular de la cuenta.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Cuenta', @level2type=N'COLUMN', @level2name=N'ClienteId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Banco al que pertenece. En este taller se registran aquí las cuentas locales.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Cuenta', @level2type=N'COLUMN', @level2name=N'BancoId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Número tratado como texto para conservar ceros iniciales, como 001.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Cuenta', @level2type=N'COLUMN', @level2name=N'NumeroCuenta';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Corresponde a Cuenta.Activa.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Cuenta', @level2type=N'COLUMN', @level2name=N'Activa';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Alcance inicial: únicamente soles. El modelo no realiza conversión de moneda.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Cuenta', @level2type=N'COLUMN', @level2name=N'Moneda';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Saldo actual. Debe actualizarse en la misma transacción que registra el movimiento.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Cuenta', @level2type=N'COLUMN', @level2name=N'SaldoDisponible';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Token binario de concurrencia para EF; no representa una fecha.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Cuenta', @level2type=N'COLUMN', @level2name=N'VersionFila';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Fecha de creación en UTC.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Cuenta', @level2type=N'COLUMN', @level2name=N'FechaCreacion';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Catálogo que hace legibles los estados de la transferencia y conserva la correspondencia numérica con el enum actual del dominio.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'EstadoTransferencia';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Valor actual del enum: 0 Rechazada, 1 RequiereConfirmacion, 2 NoDisponible, 3 Pendiente, 4 Completada.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'EstadoTransferencia', @level2type=N'COLUMN', @level2name=N'EstadoTransferenciaId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Nombre del estado actual del dominio.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'EstadoTransferencia', @level2type=N'COLUMN', @level2name=N'Nombre';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Explicación del estado para revisión del equipo.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'EstadoTransferencia', @level2type=N'COLUMN', @level2name=N'Descripcion';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Catálogo de la respuesta de antifraude. Mantiene separados los resultados de riesgo y los estados financieros de una transferencia.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'NivelRiesgo';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Valor del enum actual: 0 Bajo, 1 Medio, 2 Alto.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'NivelRiesgo', @level2type=N'COLUMN', @level2name=N'NivelRiesgoId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Nombre del nivel de riesgo.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'NivelRiesgo', @level2type=N'COLUMN', @level2name=N'Nombre';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Interpretación del resultado para el taller.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'NivelRiesgo', @level2type=N'COLUMN', @level2name=N'Descripcion';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Almacena cada solicitud y su último estado. El GUID recibido como TransactionReference es único y permite reconocer reintentos de la misma operación.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Transferencia';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'ID enviado por el consumidor. Debe conservarse en reintentos; no generar otro al repetir.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Transferencia', @level2type=N'COLUMN', @level2name=N'TransferenciaId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Cliente solicitante y titular obligatorio de la cuenta origen.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Transferencia', @level2type=N'COLUMN', @level2name=N'ClienteId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Cuenta local desde la que se solicita el débito.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Transferencia', @level2type=N'COLUMN', @level2name=N'CuentaOrigenId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Banco receptor.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Transferencia', @level2type=N'COLUMN', @level2name=N'BancoDestinoId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Identificación del destino externo o interno; no exige que exista una cuenta externa localmente.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Transferencia', @level2type=N'COLUMN', @level2name=N'NumeroCuentaDestino';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Referencia opcional cuando el destino está administrado por el core local. La FK comprueba banco y número.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Transferencia', @level2type=N'COLUMN', @level2name=N'CuentaDestinoInternaId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Monto principal que recibe el destino.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Transferencia', @level2type=N'COLUMN', @level2name=N'Monto';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Comisión calculada por el dominio y conservada como valor histórico.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Transferencia', @level2type=N'COLUMN', @level2name=N'Comision';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Total solicitado al origen; campo calculado, no se envía desde EF al insertar.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Transferencia', @level2type=N'COLUMN', @level2name=N'TotalDebito';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Moneda única del taller: PEN.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Transferencia', @level2type=N'COLUMN', @level2name=N'Moneda';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Momento de la solicitud. Debe preservarse para conciliar y auditar.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Transferencia', @level2type=N'COLUMN', @level2name=N'FechaSolicitud';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Día de Lima que agrupa el límite diario; calculado a partir de la solicitud.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Transferencia', @level2type=N'COLUMN', @level2name=N'FechaNegocio';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Estado actual. Debe indicarse explícitamente al crear la operación.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Transferencia', @level2type=N'COLUMN', @level2name=N'EstadoTransferenciaId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Motivo del resultado; no incluir credenciales ni OTP.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Transferencia', @level2type=N'COLUMN', @level2name=N'Mensaje';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Compatibilidad con el dominio actual; los intentos de entrega se registran aparte.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Transferencia', @level2type=N'COLUMN', @level2name=N'Notificada';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Token de concurrencia para impedir que EF sobrescriba cambios de estado sin detectarlos.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'Transferencia', @level2type=N'COLUMN', @level2name=N'VersionFila';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Conserva el historial de estados sin depender solo de la última fila de Transferencia. Permite explicar rechazos, confirmaciones y resultados inciertos.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'AuditoriaTransferencia';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Secuencia del evento de auditoría.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'AuditoriaTransferencia', @level2type=N'COLUMN', @level2name=N'AuditoriaTransferenciaId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Operación auditada.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'AuditoriaTransferencia', @level2type=N'COLUMN', @level2name=N'TransferenciaId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Estado registrado en este evento.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'AuditoriaTransferencia', @level2type=N'COLUMN', @level2name=N'EstadoTransferenciaId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Momento del evento en UTC.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'AuditoriaTransferencia', @level2type=N'COLUMN', @level2name=N'FechaEvento';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Descripción del evento y su motivo, sin datos secretos.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'AuditoriaTransferencia', @level2type=N'COLUMN', @level2name=N'Mensaje';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Identificador de correlación de la solicitud; no contiene el token JWT.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'AuditoriaTransferencia', @level2type=N'COLUMN', @level2name=N'TraceIdentifier';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Componente o proceso que generó el evento.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'AuditoriaTransferencia', @level2type=N'COLUMN', @level2name=N'Origen';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Registra cada efecto sobre un saldo y separa monto, comisión y crédito interno. La unicidad impide registrar dos veces el mismo concepto para una cuenta y operación.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'MovimientoCuenta';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Secuencia interna del movimiento.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'MovimientoCuenta', @level2type=N'COLUMN', @level2name=N'MovimientoCuentaId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Operación que origina el movimiento.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'MovimientoCuenta', @level2type=N'COLUMN', @level2name=N'TransferenciaId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Cuenta local afectada.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'MovimientoCuenta', @level2type=N'COLUMN', @level2name=N'CuentaId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'DEBITO_MONTO, DEBITO_COMISION, CREDITO_DESTINO o sus reversos. Un reverso necesita una decisión explícita de conciliación.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'MovimientoCuenta', @level2type=N'COLUMN', @level2name=N'TipoMovimiento';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'D disminuye el saldo; C lo incrementa.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'MovimientoCuenta', @level2type=N'COLUMN', @level2name=N'Naturaleza';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Valor positivo del movimiento. Una comisión cero no genera movimiento.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'MovimientoCuenta', @level2type=N'COLUMN', @level2name=N'Monto';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Saldo antes de aplicar este movimiento.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'MovimientoCuenta', @level2type=N'COLUMN', @level2name=N'SaldoAnterior';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Saldo después del movimiento; el CHECK exige consistencia aritmética.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'MovimientoCuenta', @level2type=N'COLUMN', @level2name=N'SaldoPosterior';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Momento de contabilización en UTC.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'MovimientoCuenta', @level2type=N'COLUMN', @level2name=N'FechaMovimiento';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Prepara la persistencia de solicitudes y respuestas del futuro evaluador o batch. La falta de respuesta se distingue de un resultado de riesgo Bajo.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'EvaluacionFraude';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Secuencia de evaluación.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'EvaluacionFraude', @level2type=N'COLUMN', @level2name=N'EvaluacionFraudeId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Operación que debe evaluarse antes del débito.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'EvaluacionFraude', @level2type=N'COLUMN', @level2name=N'TransferenciaId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Número de intento de evaluación dentro de la operación.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'EvaluacionFraude', @level2type=N'COLUMN', @level2name=N'NumeroIntento';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Pendiente, Procesando, Completada o Error. Es independiente del estado financiero.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'EvaluacionFraude', @level2type=N'COLUMN', @level2name=N'EstadoEvaluacion';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Resultado del evaluador. Es obligatorio al completar y nulo mientras no exista un resultado.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'EvaluacionFraude', @level2type=N'COLUMN', @level2name=N'NivelRiesgoId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Razón de la decisión o del error; no guardar datos de autenticación.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'EvaluacionFraude', @level2type=N'COLUMN', @level2name=N'Motivo';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Versión del conjunto de reglas utilizado; debe proporcionarla el evaluador.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'EvaluacionFraude', @level2type=N'COLUMN', @level2name=N'VersionRegla';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Identifica al script, batch o servicio que realiza la evaluación.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'EvaluacionFraude', @level2type=N'COLUMN', @level2name=N'Proveedor';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Fecha de creación de la solicitud de riesgo.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'EvaluacionFraude', @level2type=N'COLUMN', @level2name=N'FechaSolicitud';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Momento en que terminó la evaluación o se registró su error.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'EvaluacionFraude', @level2type=N'COLUMN', @level2name=N'FechaRespuesta';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Token para coordinar cambios de estado entre consumidores.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'EvaluacionFraude', @level2type=N'COLUMN', @level2name=N'VersionFila';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Conserva los avisos por entregar para permitir reintentos sin cambiar el resultado financiero. Prepara el patrón de mensajes pendientes; el worker se implementará después.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'NotificacionCliente';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Identificador del aviso.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'NotificacionCliente', @level2type=N'COLUMN', @level2name=N'NotificacionClienteId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Operación que se comunica.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'NotificacionCliente', @level2type=N'COLUMN', @level2name=N'TransferenciaId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Evento preciso que originó el aviso.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'NotificacionCliente', @level2type=N'COLUMN', @level2name=N'AuditoriaTransferenciaId';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'SMS o Correo; cada evento puede generar un aviso por canal.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'NotificacionCliente', @level2type=N'COLUMN', @level2name=N'Canal';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Pendiente, Enviada o Error.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'NotificacionCliente', @level2type=N'COLUMN', @level2name=N'EstadoEnvio';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Número de intentos de entrega.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'NotificacionCliente', @level2type=N'COLUMN', @level2name=N'Intentos';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Momento de creación en UTC.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'NotificacionCliente', @level2type=N'COLUMN', @level2name=N'FechaCreacion';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Fecha de aceptación por el proveedor; no prueba lectura por el cliente.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'NotificacionCliente', @level2type=N'COLUMN', @level2name=N'FechaEnvio';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Momento propuesto para reintentar.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'NotificacionCliente', @level2type=N'COLUMN', @level2name=N'ProximoIntento';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Resumen del error de entrega sin secretos.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'NotificacionCliente', @level2type=N'COLUMN', @level2name=N'UltimoError';
    EXEC sys.sp_addextendedproperty @name=N'MS_Description', @value=N'Token para coordinar reintentos entre consumidores.', @level0type=N'SCHEMA', @level0name=N'transferencias', @level1type=N'TABLE', @level1name=N'NotificacionCliente', @level2type=N'COLUMN', @level2name=N'VersionFila';

    COMMIT TRANSACTION;
    PRINT N'Modelo creado correctamente: 11 tablas en BD_Equipo2.transferencias.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
