using Microsoft.Data.SqlClient;

namespace SqlServerInfrastructure.Persistence;

/// <summary>Impide configurar por accidente una base distinta de la autorizada para el equipo.</summary>
public static class TransferenciasConnectionString
{
    /// <summary>Valida el destino sin abrir conexiones ni incluir credenciales en mensajes de error.</summary>
    public static string Validar(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "Configure ConnectionStrings__Transferencias con una conexión a BD_Equipo2.");

        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("La cadena de conexión de transferencias no tiene un formato SQL Server válido.");
        }

        if (!string.Equals(builder.InitialCatalog, "BD_Equipo2", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("El destino de transferencias debe ser exclusivamente BD_Equipo2.");
        if (string.IsNullOrWhiteSpace(builder.DataSource))
            throw new InvalidOperationException("Debe indicar el servidor SQL Server en la cadena de conexión.");
        if (!string.IsNullOrEmpty(builder.AttachDBFilename))
            throw new InvalidOperationException("No se permite adjuntar archivos de base de datos desde esta conexión.");

        return builder.ConnectionString;
    }
}
