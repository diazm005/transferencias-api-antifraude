using System.Data;
using System.Text.RegularExpressions;
using Domain.Entities.Internals;
using Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using SqlServerInfrastructure.Persistence;

if (args.SequenceEqual(new[] { "--sql-read-only" }))
{
    Environment.ExitCode = await ValidacionSql.EjecutarAsync();
    return;
}
if (args.SequenceEqual(new[] { "--sql-http-rollback" }))
{
    Environment.ExitCode = await PruebaSqlHttp.EjecutarAsync();
    return;
}
if (args.Length != 0) throw new ArgumentException("Argumentos admitidos: --sql-read-only, --sql-http-rollback o ninguno para las pruebas offline.");

// Estas comprobaciones leen metadatos y reglas puras: nunca abren una conexión ni ejecutan SQL.
var total = 0;
void Comprobar(bool condicion, string nombre)
{
    if (!condicion) throw new InvalidOperationException("FALLÓ: " + nombre);
    Console.WriteLine("OK: " + nombre);
    total++;
}

void Rechaza<TException>(Action accion, string nombre) where TException : Exception
{
    try { accion(); }
    catch (TException) { Comprobar(true, nombre); return; }
    Comprobar(false, nombre);
}

// Un servidor ficticio permite construir el modelo; no se invoca Open, CanConnect, EnsureCreated ni Migrate.
const string conexion = "Server=servidor-ficticio;Database=BD_Equipo2;Integrated Security=True;TrustServerCertificate=True";
using var contexto = new TransferenciasDbContext(new DbContextOptionsBuilder<TransferenciasDbContext>()
    .UseSqlServer(conexion).Options);
var modelo = contexto.GetService<IDesignTimeModel>().Model;
var entidades = modelo.GetEntityTypes().ToArray();
var sql = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "schema.sql"));
var tablas = Regex.Matches(sql, @"CREATE TABLE transferencias\.(?<tabla>\w+)\s*\((?<cuerpo>.*?)^    \);",
    RegexOptions.Singleline | RegexOptions.Multiline);
Comprobar(tablas.Count == 11 && entidades.Length == 11, "Las once entidades corresponden al modelo SQL revisado");

IEntityType Entidad(string tabla) => entidades.Single(e => e.GetTableName() == tabla);
static string Normalizar(string valor) => Regex.Replace(valor, @"[\s\[\]()]", "").ToUpperInvariant();
static string[] Columnas(string valor) => valor.Split(',').Select(c => c.Trim()).ToArray();
static string Nombres(IEnumerable<IProperty> propiedades) => string.Join(",", propiedades.Select(p => p.Name));

// La fuente de verdad de estas pruebas es el DDL existente, no una segunda copia del mapeo EF.
foreach (Match tabla in tablas)
{
    var nombre = tabla.Groups["tabla"].Value;
    var entidad = Entidad(nombre);
    var identificador = StoreObjectIdentifier.Table(nombre, "transferencias");
    Comprobar(entidad.GetSchema() == "transferencias", $"{nombre}: esquema exclusivo de transferencias");
    var columnas = Regex.Matches(tabla.Groups["cuerpo"].Value,
        @"^        (?<columna>\w+)\s+(?<tipo>tinyint|bigint|int|uniqueidentifier|varchar\(\d+\)|nvarchar\(\d+\)|char\(\d+\)|decimal\(\d+,\d+\)|datetimeoffset\(\d+\)|rowversion|bit|AS)\b?(?<resto>[^\r\n]*)",
        RegexOptions.Multiline);
    var esperadas = columnas.Select(c => c.Groups["columna"].Value).Order().ToArray();
    var reales = entidad.GetProperties().Select(p => p.GetColumnName(identificador)).Order().ToArray();
    Comprobar(esperadas.SequenceEqual(reales), $"{nombre}: columnas exactas, sin columnas de sombra adicionales");
    foreach (Match columna in columnas)
    {
        var propiedad = entidad.GetProperties().Single(p => p.GetColumnName(identificador) == columna.Groups["columna"].Value);
        var tipo = columna.Groups["tipo"].Value;
        if (tipo == "AS") continue;
        Comprobar(Normalizar(propiedad.GetColumnType()!) == Normalizar(tipo),
            $"{nombre}.{propiedad.Name}: tipo SQL compatible ({tipo})");
    }
}
Comprobar(entidades.Sum(e => e.GetProperties().Count()) == 84, "Se conservan las 84 columnas del modelo original");

// Las claves y relaciones compuestas impiden asociar operaciones con otros titulares o eventos ajenos.
var relaciones = Regex.Matches(sql,
    @"CONSTRAINT (?<nombre>FK_\w+) FOREIGN KEY \((?<columnas>[^)]+)\) REFERENCES transferencias\.(?<tabla>\w+) \((?<principales>[^)]+)\)");
var relacionesEf = entidades.SelectMany(e => e.GetForeignKeys()).ToArray();
Comprobar(relaciones.Count == 16 && relacionesEf.Length == 16, "Se conservan las dieciséis claves foráneas");
foreach (Match relacion in relaciones)
{
    var nombre = relacion.Groups["nombre"].Value;
    var fk = relacionesEf.Single(f => f.GetConstraintName() == nombre);
    Comprobar(Nombres(fk.Properties) == string.Join(",", Columnas(relacion.Groups["columnas"].Value)) &&
        fk.PrincipalEntityType.GetTableName() == relacion.Groups["tabla"].Value &&
        Nombres(fk.PrincipalKey.Properties) == string.Join(",", Columnas(relacion.Groups["principales"].Value)),
        $"{nombre}: columnas y referencia exactas");
    Comprobar(fk.DeleteBehavior is DeleteBehavior.NoAction or DeleteBehavior.Restrict,
        $"{nombre}: conserva historial sin borrado en cascada");
}

var unicas = Regex.Matches(sql, @"CONSTRAINT (?<nombre>UQ_\w+) UNIQUE \((?<columnas>[^)]+)\)");
var clavesAlternas = entidades.SelectMany(e => e.GetKeys()).Where(k => !k.IsPrimaryKey()).ToArray();
Comprobar(unicas.Count == clavesAlternas.Length, "Se conservan todas las restricciones UNIQUE");
foreach (Match unica in unicas)
{
    var clave = clavesAlternas.Single(k => k.GetName() == unica.Groups["nombre"].Value);
    Comprobar(Nombres(clave.Properties) == string.Join(",", Columnas(unica.Groups["columnas"].Value)),
        $"{unica.Groups["nombre"].Value}: unicidad del contrato SQL");
}

var indices = Regex.Matches(sql,
    @"CREATE (?<unico>UNIQUE )?INDEX (?<nombre>\w+) ON transferencias\.(?<tabla>\w+) \((?<columnas>[^)]+)\)(?: INCLUDE \((?<incluidas>[^)]+)\))?(?: WHERE (?<filtro>[^;\r\n]+))?;");
Comprobar(indices.Count == entidades.Sum(e => e.GetIndexes().Count()), "Sin índices adicionales a los del modelo SQL");
foreach (Match indice in indices)
{
    var nombre = indice.Groups["nombre"].Value;
    var ef = Entidad(indice.Groups["tabla"].Value).GetIndexes().Single(i => i.GetDatabaseName() == nombre);
    Comprobar(Nombres(ef.Properties) == string.Join(",", Columnas(indice.Groups["columnas"].Value)) &&
        ef.IsUnique == indice.Groups["unico"].Success &&
        Normalizar(ef.GetFilter() ?? "") == Normalizar(indice.Groups["filtro"].Value) &&
        string.Join(",", ef.GetIncludeProperties() ?? []) == string.Join(",", Columnas(indice.Groups["incluidas"].Value).Where(s => s.Length > 0)),
        $"{nombre}: columnas, unicidad, filtro e inclusiones correctos");
}

var transferencia = Entidad("Transferencia");
var id = transferencia.FindPrimaryKey()!.Properties.Single();
Comprobar(id.ClrType == typeof(Guid) && id.ValueGenerated == ValueGenerated.Never,
    "Transferencia: conserva el GUID recibido y no genera otro al reintentar");
var totalDebito = transferencia.FindProperty("TotalDebito")!;
Comprobar(Normalizar(totalDebito.GetComputedColumnSql()!) == "MONTO+COMISION" && totalDebito.GetIsStored() == true &&
    totalDebito.GetPrecision() == 19 && totalDebito.GetScale() == 2,
    "TotalDebito: SQL calcula y persiste la suma con precisión decimal(19,2)");
var dia = transferencia.FindProperty("FechaNegocio")!;
Comprobar(Normalizar(dia.GetComputedColumnSql()!) == Normalizar("CONVERT(date, SWITCHOFFSET(FechaSolicitud, '-05:00'))") &&
    dia.GetIsStored() == true && dia.ClrType == typeof(DateOnly), "FechaNegocio: SQL calcula el día de Lima");
var versiones = entidades.SelectMany(e => e.GetProperties()).Where(p => p.Name == "VersionFila").ToArray();
Comprobar(versiones.Length == 4 && versiones.All(p => p.IsConcurrencyToken &&
    p.ValueGenerated == ValueGenerated.OnAddOrUpdate && p.ClrType == typeof(byte[])),
    "Cuatro rowversion generadas por SQL y utilizadas como tokens de concurrencia");

// La selección de base de datos se valida antes de intentar conectarse.
Comprobar(TransferenciasConnectionString.Validar(conexion).Contains("BD_Equipo2"), "Admite una conexión del equipo 2");
Comprobar(TransferenciasConnectionString.Validar("Server=ficticio;Database=bd_equipo2;Integrated Security=True").Length > 0,
    "El nombre autorizado de base de datos no depende de mayúsculas");
Rechaza<InvalidOperationException>(() => TransferenciasConnectionString.Validar(null), "Rechaza una conexión ausente");
Rechaza<InvalidOperationException>(() => TransferenciasConnectionString.Validar("Server=ficticio;Database=BD_Equipo1"),
    "Rechaza bases de otro equipo");
Rechaza<InvalidOperationException>(() => TransferenciasConnectionString.Validar("Server=ficticio"), "Exige el nombre explícito de base");
Rechaza<InvalidOperationException>(() => TransferenciasConnectionString.Validar("Database=BD_Equipo2"), "Exige un servidor explícito");
Rechaza<InvalidOperationException>(() => TransferenciasConnectionString.Validar("cadena inválida"), "Rechaza formato de conexión inválido");
Rechaza<InvalidOperationException>(() => TransferenciasConnectionString.Validar("Server=ficticio;Database=BD_Equipo2;AttachDBFilename=C:\\archivo.mdf"),
    "Impide adjuntar otra base desde un archivo");

// Bordes de decisión del taller: ninguna condición de indisponibilidad se interpreta como riesgo bajo.
var casos = new (decimal Monto, int Previos, bool Conocido, NivelRiesgo Nivel)[]
{
    (0.01m, 0, false, NivelRiesgo.Bajo),
    (4_999.99m, 0, false, NivelRiesgo.Bajo),
    (5_000m, 0, false, NivelRiesgo.Medio),
    (5_000m, 0, true, NivelRiesgo.Bajo),
    (9_999.99m, 4, true, NivelRiesgo.Bajo),
    (10_000m, 0, true, NivelRiesgo.Medio),
    (39_999.99m, 0, true, NivelRiesgo.Medio),
    (40_000m, 0, true, NivelRiesgo.Alto),
    (100m, 4, true, NivelRiesgo.Bajo),
    (100m, 5, true, NivelRiesgo.Alto),
    (5_000m, 5, false, NivelRiesgo.Alto)
};
foreach (var caso in casos)
{
    var resultado = PoliticaFraude.Evaluar(new(caso.Monto, caso.Previos, caso.Conocido));
    Comprobar(resultado.Nivel == caso.Nivel && !string.IsNullOrWhiteSpace(resultado.Motivo) &&
        resultado.Motivo.Length <= 500 && resultado.VersionRegla == PoliticaFraude.Version,
        $"Fraude: S/ {caso.Monto}, {caso.Previos} débitos, destino conocido {caso.Conocido} → {caso.Nivel}, con motivo y versión");
}
Rechaza<ArgumentNullException>(() => PoliticaFraude.Evaluar(null!), "Fraude: exige contexto");
Rechaza<ArgumentOutOfRangeException>(() => PoliticaFraude.Evaluar(new(0, 0, true)), "Fraude: rechaza monto cero");
Rechaza<ArgumentOutOfRangeException>(() => PoliticaFraude.Evaluar(new(-1, 0, true)), "Fraude: rechaza monto negativo");
Rechaza<ArgumentOutOfRangeException>(() => PoliticaFraude.Evaluar(new(100, -1, true)), "Fraude: rechaza contador negativo");

Comprobar(contexto.Database.GetDbConnection().State == ConnectionState.Closed,
    "La ejecución completa conservó cerrada la conexión SQL");
Console.WriteLine($"Completadas: {total} comprobaciones offline. No se ejecutó SQL ni se modificó ninguna base de datos.");
Console.WriteLine($"Completadas: {await PruebasHttp.EjecutarAsync()} comprobaciones del cliente HTTP con respuestas simuladas.");
