/*
Datos ficticios del taller. Solo inserta los catálogos y las dos personas de la demo.
No crea transferencias, débitos, evaluaciones ni notificaciones.
Puede repetirse: si un código existe, no actualiza ni sobrescribe sus valores.
Los saldos son saldos iniciales de prueba, no movimientos bancarios reales.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
IF DB_NAME() <> N'BD_Equipo2'
    THROW 51000, N'Operación detenida: ejecutar únicamente en BD_Equipo2.', 1;
IF OBJECT_ID(N'transferencias.Transferencia', N'U') IS NULL
    THROW 51002, N'Primero ejecutar 001_crear_modelo.sql.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    INSERT INTO transferencias.EstadoTransferencia (EstadoTransferenciaId, Nombre, Descripcion)
    SELECT v.Id, v.Nombre, v.Descripcion
    FROM (VALUES
        (0, 'Rechazada', N'La operación no supera las reglas o fue rechazada explícitamente.'),
        (1, 'RequiereConfirmacion', N'No hay débito. Se necesita un OTP válido para continuar.'),
        (2, 'NoDisponible', N'No hay débito. Un servicio previo a la ejecución no respondió.'),
        (3, 'Pendiente', N'La ejecución requiere seguimiento. Consultar movimientos y proveedor antes de decidir un reintento.'),
        (4, 'Completada', N'La ejecución se confirmó según el alcance del adaptador.')
    ) v(Id, Nombre, Descripcion)
    WHERE NOT EXISTS (SELECT 1 FROM transferencias.EstadoTransferencia e WHERE e.EstadoTransferenciaId = v.Id);

    INSERT INTO transferencias.NivelRiesgo (NivelRiesgoId, Nombre, Descripcion)
    SELECT v.Id, v.Nombre, v.Descripcion
    FROM (VALUES
        (0, 'Bajo', N'Puede continuar si cumple las otras reglas.'),
        (1, 'Medio', N'Exige confirmación adicional por OTP.'),
        (2, 'Alto', N'Bloquea la transferencia antes de ejecutar el débito.')
    ) v(Id, Nombre, Descripcion)
    WHERE NOT EXISTS (SELECT 1 FROM transferencias.NivelRiesgo n WHERE n.NivelRiesgoId = v.Id);

    INSERT INTO transferencias.Banco (Codigo, Nombre, EsBancoPropio)
    SELECT v.Codigo, v.Nombre, v.EsPropio
    FROM (VALUES ('BANCO_A', N'Banco local ficticio', 1), ('BANCO_B', N'Banco externo ficticio', 0)) v(Codigo, Nombre, EsPropio)
    WHERE NOT EXISTS (SELECT 1 FROM transferencias.Banco b WHERE b.Codigo = v.Codigo);

    INSERT INTO transferencias.Segmento (Nombre, LimiteDiario)
    SELECT v.Nombre, v.Limite
    FROM (VALUES (N'Regular', CAST(15000 AS decimal(18,2))), (N'Preferente', CAST(50000 AS decimal(18,2)))) v(Nombre, Limite)
    WHERE NOT EXISTS (SELECT 1 FROM transferencias.Segmento s WHERE s.Nombre = v.Nombre);

    INSERT INTO transferencias.Cliente (Dni, Nombres, SegmentoId)
    SELECT v.Dni, v.Nombres, s.SegmentoId
    FROM (VALUES ('12345678', N'Ana Demo', N'Preferente'), ('87654321', N'Luis Demo', N'Regular')) v(Dni, Nombres, Segmento)
    JOIN transferencias.Segmento s ON s.Nombre = v.Segmento
    WHERE NOT EXISTS (SELECT 1 FROM transferencias.Cliente c WHERE c.Dni = v.Dni);

    INSERT INTO transferencias.Cuenta (ClienteId, BancoId, NumeroCuenta, Activa, SaldoDisponible)
    SELECT c.ClienteId, b.BancoId, v.Numero, v.Activa, v.Saldo
    FROM (VALUES
        ('12345678', '001', 1, CAST(100000 AS decimal(18,2))),
        ('87654321', '002', 1, CAST(2000 AS decimal(18,2))),
        ('12345678', '003', 0, CAST(20000 AS decimal(18,2)))
    ) v(Dni, Numero, Activa, Saldo)
    JOIN transferencias.Cliente c ON c.Dni = v.Dni
    JOIN transferencias.Banco b ON b.Codigo = 'BANCO_A'
    WHERE NOT EXISTS (SELECT 1 FROM transferencias.Cuenta a WHERE a.BancoId = b.BancoId AND a.NumeroCuenta = v.Numero);

    COMMIT TRANSACTION;
    PRINT N'Catálogos y datos ficticios disponibles en BD_Equipo2. No se ejecutaron transferencias.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
