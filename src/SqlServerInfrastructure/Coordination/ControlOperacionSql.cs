using System.Data;
using Domain.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SqlServerInfrastructure.Persistence;

namespace SqlServerInfrastructure.Coordination;

/// <summary>Mantiene un bloqueo SQL por referencia durante todo el caso de uso, incluso entre instancias de la API.</summary>
public sealed class ControlOperacionSql(
    IDbContextFactory<TransferenciasDbContext> factory, ILogger<ControlOperacionSql> logger) : IControlOperacion
{
    public async Task<IAsyncDisposable> AdquirirAsync(Guid operacionId, CancellationToken ct = default)
    {
        var db = await factory.CreateDbContextAsync(ct);
        try
        {
            await db.Database.OpenConnectionAsync(ct);
            var connection = (SqlConnection)db.Database.GetDbConnection();
            await using var command = connection.CreateCommand();
            command.CommandTimeout = 10;
            command.CommandText = "DECLARE @r int; EXEC @r = sys.sp_getapplock @Resource=@recurso, @LockMode='Exclusive', @LockOwner='Session', @LockTimeout=5000; SELECT @r;";
            var resource = "transferencias:operacion:" + operacionId.ToString("D");
            command.Parameters.Add("@recurso", SqlDbType.NVarChar, 255).Value = resource;
            var result = Convert.ToInt32(await command.ExecuteScalarAsync(ct));
            if (result < 0) throw new TimeoutException("La operación está siendo procesada. Reintenta con la misma referencia.");
            return new Bloqueo(db, connection, resource, logger);
        }
        catch
        {
            // Una cancelación pudo ocurrir después de adquirir el bloqueo: esa sesión no debe volver al pool.
            if (db.Database.GetDbConnection() is SqlConnection connection) SqlConnection.ClearPool(connection);
            await db.DisposeAsync();
            throw;
        }
    }

    private sealed class Bloqueo(TransferenciasDbContext db, SqlConnection connection, string resource,
        ILogger logger) : IAsyncDisposable
    {
        private int _released;
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandTimeout = 5;
                command.CommandText = "DECLARE @r int; EXEC @r = sys.sp_releaseapplock @Resource=@recurso, @LockOwner='Session'; SELECT @r;";
                command.Parameters.Add("@recurso", SqlDbType.NVarChar, 255).Value = resource;
                if (Convert.ToInt32(await command.ExecuteScalarAsync(CancellationToken.None)) < 0)
                    throw new InvalidOperationException("No se pudo liberar el bloqueo de operación.");
            }
            catch (Exception ex)
            {
                SqlConnection.ClearPool(connection);
                logger.LogWarning(ex, "Se descartó una conexión cuyo bloqueo de operación no pudo liberarse.");
            }
            finally { await db.DisposeAsync(); }
        }
    }
}
