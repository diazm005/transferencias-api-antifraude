namespace Application.Ports;

/// <summary>Identidad verificada por el adaptador de entrada. Aplicación no conoce JWT ni HttpContext.</summary>
public interface IClienteActual
{
    string? Dni { get; }
}
