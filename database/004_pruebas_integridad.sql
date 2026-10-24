/*
Pruebas de integridad exclusivas de BD_Equipo2.
Las inserciones y actualizaciones de prueba se ejecutan en una transacción
que SIEMPRE se revierte. No dejan transferencias ni cambian saldos definitivos.
Los valores IDENTITY pueden avanzar aunque haya ROLLBACK; los huecos son normales.
XACT_ABORT OFF permite continuar tras errores esperados de CHECK/FK/UNIQUE.
No se prueban aquí las futuras reglas transaccionales del repositorio EF.
*/
SET NOCOUNT ON;
IF DB_NAME() <> N'BD_Equipo2'
    THROW 51000, N'Operación detenida: ejecutar únicamente en BD_Equipo2.', 1;
IF @@TRANCOUNT <> 0
    THROW 51003, N'Ejecutar las pruebas sin otra transacción abierta.', 1;
SET XACT_ABORT OFF;

DECLARE @Resultados TABLE (Prueba nvarchar(150), Correcta bit);
DECLARE @Casos TABLE (Prueba nvarchar(150), Restriccion sysname, Sentencia nvarchar(max));
DECLARE @Origen bigint, @Destino bigint, @Cliente bigint, @OtroCliente bigint,
        @BancoLocal int, @BancoExterno int, @Evento bigint,
        @SaldoInicial decimal(18,2), @Operacion uniqueidentifier = NEWID(),
        @Operacion2 uniqueidentifier = NEWID();

SELECT @BancoLocal=BancoId FROM transferencias.Banco WHERE Codigo='BANCO_A';
SELECT @BancoExterno=BancoId FROM transferencias.Banco WHERE Codigo='BANCO_B';
SELECT @Cliente=ClienteId FROM transferencias.Cliente WHERE Dni='12345678';
SELECT @OtroCliente=ClienteId FROM transferencias.Cliente WHERE Dni='87654321';
SELECT @Origen=CuentaId, @SaldoInicial=SaldoDisponible FROM transferencias.Cuenta WHERE BancoId=@BancoLocal AND NumeroCuenta='001';
SELECT @Destino=CuentaId FROM transferencias.Cuenta WHERE BancoId=@BancoLocal AND NumeroCuenta='002';
IF @Origen IS NULL OR @Destino IS NULL OR @Cliente IS NULL OR @OtroCliente IS NULL OR @BancoExterno IS NULL
    THROW 51004, N'Faltan los datos ficticios; ejecutar primero 002_datos_demostracion.sql.', 1;
IF @SaldoInicial < 105
    THROW 51005, N'La cuenta ficticia 001 no tiene saldo suficiente para esta prueba. No se modificará.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    -- Un caso válido comprueba que el modelo admite destino externo sin Cuenta local.
    INSERT transferencias.Transferencia
        (TransferenciaId,ClienteId,CuentaOrigenId,BancoDestinoId,NumeroCuentaDestino,Monto,Comision,FechaSolicitud,EstadoTransferenciaId,Mensaje)
    VALUES
        (@Operacion,@Cliente,@Origen,@BancoExterno,'EXTERNA_PRUEBA',100,5,'2026-10-17T04:30:00+00:00',3,N'Prueba reversible');

    INSERT transferencias.Transferencia
        (TransferenciaId,ClienteId,CuentaOrigenId,BancoDestinoId,NumeroCuentaDestino,Monto,EstadoTransferenciaId)
    VALUES (@Operacion2,@Cliente,@Origen,@BancoExterno,'OTRA_PRUEBA',10,0);

    INSERT transferencias.AuditoriaTransferencia (TransferenciaId,EstadoTransferenciaId,Mensaje)
    VALUES (@Operacion,3,N'Evento de prueba');
    SET @Evento=SCOPE_IDENTITY();

    INSERT transferencias.MovimientoCuenta
        (TransferenciaId,CuentaId,TipoMovimiento,Naturaleza,Monto,SaldoAnterior,SaldoPosterior)
    VALUES (@Operacion,@Origen,'DEBITO_MONTO','D',100,@SaldoInicial,@SaldoInicial-100);
    INSERT transferencias.MovimientoCuenta
        (TransferenciaId,CuentaId,TipoMovimiento,Naturaleza,Monto,SaldoAnterior,SaldoPosterior)
    VALUES (@Operacion,@Origen,'DEBITO_COMISION','D',5,@SaldoInicial-100,@SaldoInicial-105);
    UPDATE transferencias.Cuenta SET SaldoDisponible=@SaldoInicial-105 WHERE CuentaId=@Origen;

    INSERT @Resultados VALUES (N'Destino externo válido sin cuenta local',1);
    INSERT @Resultados SELECT N'Fecha de negocio usa el día de Lima',CASE WHEN FechaNegocio='20261016' THEN 1 ELSE 0 END
    FROM transferencias.Transferencia WHERE TransferenciaId=@Operacion;
    INSERT @Resultados SELECT N'Total de débito conserva monto más comisión',CASE WHEN TotalDebito=105 THEN 1 ELSE 0 END
    FROM transferencias.Transferencia WHERE TransferenciaId=@Operacion;

    INSERT @Casos VALUES
    (N'Rechaza saldo negativo','CK_Cuenta_Saldo',
        N'UPDATE transferencias.Cuenta SET SaldoDisponible=-1 WHERE CuentaId=@Origen;'),
    (N'Rechaza monto cero','CK_Transferencia_Monto',
        N'UPDATE transferencias.Transferencia SET Monto=0 WHERE TransferenciaId=@Operacion;'),
    (N'Rechaza cuenta de otro titular','FK_Transferencia_OrigenTitular',
        N'UPDATE transferencias.Transferencia SET ClienteId=@OtroCliente WHERE TransferenciaId=@Operacion;'),
    (N'Rechaza destino igual al origen','CK_Transferencia_CuentasDiferentes',
        N'UPDATE transferencias.Transferencia SET BancoDestinoId=@BancoLocal,NumeroCuentaDestino=''001'',CuentaDestinoInternaId=@Origen WHERE TransferenciaId=@Operacion;'),
    (N'Rechaza destino interno con banco o número incorrectos','FK_Transferencia_DestinoInterno',
        N'UPDATE transferencias.Transferencia SET CuentaDestinoInternaId=@Destino WHERE TransferenciaId=@Operacion;'),
    (N'Rechaza ID de transferencia duplicado','PK_Transferencia',
        N'INSERT transferencias.Transferencia (TransferenciaId,ClienteId,CuentaOrigenId,BancoDestinoId,NumeroCuentaDestino,Monto,EstadoTransferenciaId) VALUES (@Operacion,@Cliente,@Origen,@BancoExterno,''EXTERNA_PRUEBA'',100,3);'),
    (N'Rechaza movimiento duplicado','UQ_Movimiento_OperacionCuentaTipo',
        N'INSERT transferencias.MovimientoCuenta (TransferenciaId,CuentaId,TipoMovimiento,Naturaleza,Monto,SaldoAnterior,SaldoPosterior) VALUES (@Operacion,@Origen,''DEBITO_MONTO'',''D'',100,100000,99900);'),
    (N'Rechaza movimiento con saldo mal calculado','CK_Movimiento_Calculo',
        N'INSERT transferencias.MovimientoCuenta (TransferenciaId,CuentaId,TipoMovimiento,Naturaleza,Monto,SaldoAnterior,SaldoPosterior) VALUES (@Operacion,@Destino,''CREDITO_DESTINO'',''C'',100,2000,2200);'),
    (N'Rechaza fraude completado sin nivel de riesgo','CK_Fraude_Resultado',
        N'INSERT transferencias.EvaluacionFraude (TransferenciaId,EstadoEvaluacion,VersionRegla,Proveedor,FechaRespuesta) VALUES (@Operacion,''Completada'',''prueba'',''prueba'',TODATETIMEOFFSET(SYSUTCDATETIME(),''+00:00''));'),
    (N'Rechaza aviso asociado al evento de otra operación','FK_Notificacion_Auditoria',
        N'INSERT transferencias.NotificacionCliente (TransferenciaId,AuditoriaTransferenciaId,Canal) VALUES (@Operacion2,@Evento,''Correo'');'),
    (N'Rechaza un segundo banco propio','UX_Banco_Propio',
        N'INSERT transferencias.Banco (Codigo,Nombre,EsBancoPropio) VALUES (''PRUEBA_SEGUNDO'',N''Prueba reversible'',1);'),
    (N'Rechaza documento con letras','CK_Cliente_Dni',
        N'INSERT transferencias.Cliente (Dni,Nombres,SegmentoId) SELECT ''12A45678'',N''Prueba reversible'',SegmentoId FROM transferencias.Cliente WHERE ClienteId=@Cliente;');

    DECLARE @Prueba nvarchar(150), @Restriccion sysname, @Sentencia nvarchar(max);
    DECLARE Casos CURSOR LOCAL FAST_FORWARD FOR SELECT Prueba,Restriccion,Sentencia FROM @Casos;
    OPEN Casos;
    FETCH NEXT FROM Casos INTO @Prueba,@Restriccion,@Sentencia;
    WHILE @@FETCH_STATUS=0
    BEGIN
        SAVE TRANSACTION CasoIntegridad;
        BEGIN TRY
            EXEC sys.sp_executesql @Sentencia,
                N'@Origen bigint,@Destino bigint,@Cliente bigint,@OtroCliente bigint,@BancoLocal int,@BancoExterno int,@Operacion uniqueidentifier,@Operacion2 uniqueidentifier,@Evento bigint',
                @Origen,@Destino,@Cliente,@OtroCliente,@BancoLocal,@BancoExterno,@Operacion,@Operacion2,@Evento;
            INSERT @Resultados VALUES (@Prueba,0);
        END TRY
        BEGIN CATCH
            IF ERROR_NUMBER() NOT IN (547,2601,2627) OR CHARINDEX(@Restriccion,ERROR_MESSAGE())=0
                THROW;
            INSERT @Resultados VALUES (@Prueba,1);
        END CATCH;
        ROLLBACK TRANSACTION CasoIntegridad;
        FETCH NEXT FROM Casos INTO @Prueba,@Restriccion,@Sentencia;
    END;
    CLOSE Casos;
    DEALLOCATE Casos;

    IF EXISTS (SELECT 1 FROM @Resultados WHERE Correcta=0)
        THROW 51006, N'Una restricción no detuvo una entrada inválida. Revisar el modelo.', 1;

    DECLARE @Total int=(SELECT COUNT(*) FROM @Resultados);
    SELECT Prueba, CASE WHEN Correcta=1 THEN N'Correcta' ELSE N'Falló' END AS Resultado FROM @Resultados;
    ROLLBACK TRANSACTION;
    IF EXISTS (SELECT 1 FROM transferencias.Transferencia WHERE TransferenciaId IN (@Operacion,@Operacion2))
        THROW 51007, N'La reversión de pruebas no fue completa.', 1;
    IF (SELECT SaldoDisponible FROM transferencias.Cuenta WHERE CuentaId=@Origen) <> @SaldoInicial
        THROW 51008, N'El saldo no coincide con el valor anterior a la prueba.', 1;
    SELECT @Total AS PruebasCorrectas, @@TRANCOUNT AS TransaccionesAbiertas, N'Filas de prueba revertidas; saldo original conservado.' AS Resultado;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
SET XACT_ABORT ON;
