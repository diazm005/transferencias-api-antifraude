using Application.Internals.Adapters;

/* ********************************************************************************************************
# * Copyright © 2026 Arify Labs - Todos los derechos reservados.
# *
# * Descripción                  : Clase EasyResult para unificar respuestas exitosas y de error
# *
# * Autor                    : Victor Jhampier Caxi Maquera
# * Correo/Móvil/Teléfono    : victorjhampier@gmail.com | 968991714
# *
# * Fecha de creación         : 01/01/2026
# *
# **********************************************************************************************************/

namespace Application.Internals.Executors;

public sealed class EasyResult<T>
{
    public bool IsSuccess { get; set; }
    public int Status { get; set; }
    public T? SuccessValue { get; set; }
    public IReadOnlyCollection<ValidationResultAdapter> ValidationValues { get; set; } = Array.Empty<ValidationResultAdapter>();

    public static EasyResult<T> Success(T successValue) => new()
    {
        IsSuccess = true,
        Status = 200,
        SuccessValue = successValue
    };

    // Resultado con estado HTTP propio; también sirve para resultados de negocio con cuerpo (por ejemplo, 202 o 422).
    public static EasyResult<T> Success(T successValue, int status) => new()
    {
        IsSuccess = status < 400,
        Status = status,
        SuccessValue = successValue
    };

    public static EasyResult<T> Failure(int status, string code, string message) =>
        Failure(status, [new ValidationResultAdapter { Code = code, Message = message }]);

    public static EasyResult<T> Failure(int status, IReadOnlyCollection<ValidationResultAdapter> validationValues) => new()
    {
        IsSuccess = false,
        Status = status,
        ValidationValues = validationValues
    };

    public static EasyResult<T> Empty() => new()
    {
        IsSuccess = true,
        Status = 204
    };
}
