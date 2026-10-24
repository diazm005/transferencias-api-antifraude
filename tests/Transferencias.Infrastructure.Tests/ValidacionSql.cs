using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SqlServerInfrastructure.Persistence;
using SqlServerInfrastructure.Repositories;

/// <summary>Comprueba el esquema desplegado y las consultas EF con SELECT; nunca crea ni modifica datos.</summary>
internal static class ValidacionSql
{
    public static async Task<int> EjecutarAsync()
    {
        try
        {
            var connection = TransferenciasConnectionString.Validar(
                Environment.GetEnvironmentVariable("ConnectionStrings__Transferencias"));
            var options = new DbContextOptionsBuilder<TransferenciasDbContext>()
                .UseSqlServer(connection, sql => sql.CommandTimeout(15)).Options;
            await using var db = new TransferenciasDbContext(options);
            await db.Database.OpenConnectionAsync();
            var actual = await db.Database.SqlQueryRaw<string>("SELECT CONVERT(nvarchar(128), DB_NAME()) AS [Value]").SingleAsync();
            if (!string.Equals(actual, "BD_Equipo2", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("La sesión no está en BD_Equipo2; se detiene la validación.");
            Console.WriteLine("Conexión SQL confirmada exclusivamente en BD_Equipo2. Modo solo lectura.");

            var columns = await db.Database.SqlQueryRaw<Columna>("""
                SELECT t.name AS Tabla, c.name AS Nombre, ty.name AS Tipo,
                    CONVERT(int,c.max_length) AS Longitud, CONVERT(int,c.precision) AS Precision,
                    CONVERT(int,c.scale) AS Escala, c.is_nullable AS Anulable,
                    c.is_identity AS Identidad, c.is_computed AS Calculada,
                    CONVERT(bit,ISNULL(cc.is_persisted,0)) AS Persistida
                FROM sys.tables t
                JOIN sys.schemas s ON s.schema_id=t.schema_id
                JOIN sys.columns c ON c.object_id=t.object_id
                JOIN sys.types ty ON ty.user_type_id=c.user_type_id
                LEFT JOIN sys.computed_columns cc ON cc.object_id=c.object_id AND cc.column_id=c.column_id
                WHERE s.name=N'transferencias'
                """).ToListAsync();
            var model = db.GetService<IDesignTimeModel>().Model;
            var differences = new List<string>();
            foreach (var entity in model.GetEntityTypes())
            {
                var table = entity.GetTableName()!;
                var tableId = StoreObjectIdentifier.Table(table, "transferencias");
                foreach (var property in entity.GetProperties())
                {
                    var name = property.GetColumnName(tableId)!;
                    var column = columns.SingleOrDefault(x => x.Tabla == table && x.Nombre == name);
                    if (column is null) { differences.Add($"Falta {table}.{name}."); continue; }
                    var type = column.Tipo.ToLowerInvariant() switch
                    {
                        "nvarchar" or "nchar" => $"{column.Tipo}({(column.Longitud == -1 ? "max" : (column.Longitud / 2).ToString())})",
                        "varchar" or "char" or "varbinary" or "binary" => $"{column.Tipo}({(column.Longitud == -1 ? "max" : column.Longitud.ToString())})",
                        "decimal" or "numeric" => $"decimal({column.Precision},{column.Escala})",
                        "datetimeoffset" or "datetime2" or "time" => $"{column.Tipo}({column.Escala})",
                        "timestamp" => "rowversion",
                        _ => column.Tipo
                    };
                    if (Normalizar(type) != Normalizar(property.GetColumnType()!))
                        differences.Add($"Tipo distinto en {table}.{name}: SQL={type}, EF={property.GetColumnType()}.");
                    if (!column.Calculada && column.Anulable != property.IsNullable)
                        differences.Add($"Nulabilidad distinta en {table}.{name}.");
                    if (column.Calculada != (property.GetComputedColumnSql() is not null) ||
                        column.Calculada && column.Persistida != (property.GetIsStored() == true))
                        differences.Add($"Cálculo/persistencia distintos en {table}.{name}.");
                    if (column.Identidad != (property.GetValueGenerationStrategy() == SqlServerValueGenerationStrategy.IdentityColumn))
                        differences.Add($"Identidad distinta en {table}.{name}.");
                }
                var extras = columns.Where(x => x.Tabla == table && !entity.GetProperties()
                    .Any(p => p.GetColumnName(tableId) == x.Nombre));
                differences.AddRange(extras.Select(x => $"Columna SQL sin mapear: {table}.{x.Nombre}."));
            }
            var keys = await db.Database.SqlQueryRaw<Relacion>("""
                SELECT fk.name AS Nombre, t.name AS Origen, c.name AS Columna,
                    rt.name AS Destino, rc.name AS Referencia,
                    fkc.constraint_column_id AS Orden, fk.is_disabled AS Deshabilitada,
                    fk.is_not_trusted AS SinValidar, fk.delete_referential_action_desc AS AlEliminar
                FROM sys.foreign_keys fk
                JOIN sys.tables t ON t.object_id=fk.parent_object_id
                JOIN sys.schemas s ON s.schema_id=t.schema_id
                JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id=fk.object_id
                JOIN sys.columns c ON c.object_id=t.object_id AND c.column_id=fkc.parent_column_id
                JOIN sys.tables rt ON rt.object_id=fk.referenced_object_id
                JOIN sys.columns rc ON rc.object_id=rt.object_id AND rc.column_id=fkc.referenced_column_id
                WHERE s.name=N'transferencias'
                """).ToListAsync();
            foreach (var fk in model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
            {
                var deployed = keys.Where(x => x.Nombre == fk.GetConstraintName()).OrderBy(x => x.Orden).ToArray();
                if (deployed.Length != fk.Properties.Count ||
                    !deployed.Select(x => x.Columna).SequenceEqual(fk.Properties.Select(x => x.Name)) ||
                    !deployed.Select(x => x.Referencia).SequenceEqual(fk.PrincipalKey.Properties.Select(x => x.Name)) ||
                    deployed.Any(x => x.Origen != fk.DeclaringEntityType.GetTableName() ||
                        x.Destino != fk.PrincipalEntityType.GetTableName() || x.Deshabilitada || x.SinValidar || x.AlEliminar != "NO_ACTION"))
                    differences.Add($"Relación ausente, distinta o deshabilitada: {fk.GetConstraintName()}.");
            }
            if (differences.Count != 0)
            {
                differences.ForEach(x => Console.WriteLine("DIFERENCIA: " + x));
                return 1;
            }
            Console.WriteLine($"EF coincide con {model.GetEntityTypes().Count()} tablas, {columns.Count} columnas y {keys.Select(x => x.Nombre).Distinct().Count()} relaciones desplegadas.");

            // Materializar una fila por entidad prueba que EF puede leer los tipos reales.
            await db.Bancos.AsNoTracking().Take(1).ToListAsync();
            await db.Segmentos.AsNoTracking().Take(1).ToListAsync();
            var customers = await db.Clientes.AsNoTracking().Take(1).ToListAsync();
            var accounts = await db.Cuentas.AsNoTracking().Take(1).ToListAsync();
            var states = await db.EstadosTransferencia.AsNoTracking().Select(x => (int)x.EstadoTransferenciaId).ToListAsync();
            var risks = await db.NivelesRiesgo.AsNoTracking().Select(x => (int)x.NivelRiesgoId).ToListAsync();
            await db.Transferencias.AsNoTracking().Take(1).ToListAsync();
            await db.AuditoriasTransferencia.AsNoTracking().Take(1).ToListAsync();
            await db.MovimientosCuenta.AsNoTracking().Take(1).ToListAsync();
            await db.EvaluacionesFraude.AsNoTracking().Take(1).ToListAsync();
            await db.NotificacionesCliente.AsNoTracking().Take(1).ToListAsync();
            if (!Enumerable.Range(0, 5).All(states.Contains) || !Enumerable.Range(0, 3).All(risks.Contains))
                throw new InvalidOperationException("Faltan valores de los catálogos de estados o riesgo.");

            var factory = new Fabrica(options);
            var core = new CoreBancarioEfRepository(factory, TimeProvider.System);
            if (customers.FirstOrDefault() is { } customer)
            {
                await core.ObtenerClienteAsync(customer.Dni);
                await core.ObtenerAcumuladoAsync(customer.Dni, DateOnly.FromDateTime(
                    DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-5)).DateTime));
            }
            if (accounts.FirstOrDefault() is { } account) await core.ObtenerCuentaAsync(account.NumeroCuenta);
            await new AuditoriaEfRepository(factory, TimeProvider.System).ObtenerAsync(Guid.Empty);
            Console.WriteLine("Lecturas EF y consultas de repositorios correctas. Ningún INSERT, UPDATE, DELETE ni DDL ejecutado.");
            return 0;
        }
        catch (SqlException ex)
        {
            Console.Error.WriteLine($"No se completó la validación SQL (código {ex.Number}). Credenciales omitidas.");
            if (ex.Number == -2146893019 || ex.Message.Contains("certificate", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("certificado", StringComparison.OrdinalIgnoreCase))
                Console.Error.WriteLine("La conexión TLS no pudo validar el certificado del servidor.");
            else if (ex.Number == 18456) Console.Error.WriteLine("SQL Server rechazó la autenticación.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Validación interrumpida ({ex.GetType().Name}). No se modificaron datos.");
            return 1;
        }
    }

    private static string Normalizar(string type) => Regex.Replace(type, @"[\s\[\]]", "").ToUpperInvariant();
    private sealed class Fabrica(DbContextOptions<TransferenciasDbContext> options) : IDbContextFactory<TransferenciasDbContext>
    {
        public TransferenciasDbContext CreateDbContext() => new(options);
    }
    private sealed class Columna
    {
        public string Tabla { get; set; } = "";
        public string Nombre { get; set; } = "";
        public string Tipo { get; set; } = "";
        public int Longitud { get; set; }
        public int Precision { get; set; }
        public int Escala { get; set; }
        public bool Anulable { get; set; }
        public bool Identidad { get; set; }
        public bool Calculada { get; set; }
        public bool Persistida { get; set; }
    }
    private sealed class Relacion
    {
        public string Nombre { get; set; } = "";
        public string Origen { get; set; } = "";
        public string Columna { get; set; } = "";
        public string Destino { get; set; } = "";
        public string Referencia { get; set; } = "";
        public int Orden { get; set; }
        public bool Deshabilitada { get; set; }
        public bool SinValidar { get; set; }
        public string AlEliminar { get; set; } = "";
    }
}
