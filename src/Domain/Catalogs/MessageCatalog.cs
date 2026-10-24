namespace Domain.Catalogs;

public static class MessageCatalog
{
    private static readonly Dictionary<int, string> codigoErrores = new Dictionary<int, string>()
    {
        {21001, "Cannot be null"},
        {21002, "Cannot be empty"},
        {21003, "It is out of the allowed bounds"},
        {21004, "Allowed minimum length"},
        {21005, "Allowed maximum length"},
        {21006, "Invalid type"},
        {21007, "Invalid value"},
        {21008, "Invalid name"},
        {21009, "Invalid"},
        {21010, "Use alphanumeric characters (a-z and 0-9)"},
        {21011, "Use allowed characters (a-z)"},
        {21012, "Use numeric characters"},
        {21096, "Does not authorize data processing"},
        {21097, "Incorrect parameters"},
        {21098, "General internal error"}
    };

    public static string GetErrorByCode(int nCode, string cName = "elemento")
    {
        if (codigoErrores.TryGetValue(nCode, out string? result))
            return result.Replace("##", cName);
        return "Error desconocido en ##".Replace("##", cName);
    }
}
