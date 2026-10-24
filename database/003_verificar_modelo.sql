/*
Verificación de metadatos y datos iniciales. Solo lectura.
Las consultas muestran tablas, relaciones y documentación creada.
No sustituyen las pruebas de la futura integración EF.
*/
SET NOCOUNT ON;
IF DB_NAME() <> N'BD_Equipo2'
    THROW 51000, N'Operación detenida: ejecutar únicamente en BD_Equipo2.', 1;

SELECT DB_NAME() AS BaseActual,
       (SELECT COUNT(*) FROM sys.tables WHERE schema_id=SCHEMA_ID(N'transferencias')) AS Tablas,
       (SELECT COUNT(*) FROM sys.columns c JOIN sys.tables t ON t.object_id=c.object_id WHERE t.schema_id=SCHEMA_ID(N'transferencias')) AS Columnas,
       (SELECT COUNT(*) FROM sys.foreign_keys WHERE schema_id=SCHEMA_ID(N'transferencias')) AS Relaciones,
       (SELECT COUNT(*) FROM sys.extended_properties ep JOIN sys.tables t ON t.object_id=ep.major_id WHERE t.schema_id=SCHEMA_ID(N'transferencias') AND ep.name=N'MS_Description') AS Descripciones;

SELECT t.name AS Tabla, CAST(ep.value AS nvarchar(4000)) AS Motivo
FROM sys.tables t
LEFT JOIN sys.extended_properties ep ON ep.major_id=t.object_id AND ep.minor_id=0 AND ep.name=N'MS_Description'
WHERE t.schema_id=SCHEMA_ID(N'transferencias')
ORDER BY t.name;

SELECT fk.name AS Relacion, OBJECT_NAME(fk.parent_object_id) AS TablaOrigen,
       OBJECT_NAME(fk.referenced_object_id) AS TablaReferenciada,
       fk.is_disabled AS Deshabilitada, fk.is_not_trusted AS SinValidar
FROM sys.foreign_keys fk
WHERE fk.schema_id=SCHEMA_ID(N'transferencias')
ORDER BY TablaOrigen, Relacion;

SELECT c.Dni, c.Nombres, s.Nombre AS Segmento, a.NumeroCuenta, b.Codigo AS Banco,
       a.Activa, a.Moneda, a.SaldoDisponible
FROM transferencias.Cuenta a
JOIN transferencias.Cliente c ON c.ClienteId=a.ClienteId
JOIN transferencias.Segmento s ON s.SegmentoId=c.SegmentoId
JOIN transferencias.Banco b ON b.BancoId=a.BancoId
ORDER BY a.NumeroCuenta;
