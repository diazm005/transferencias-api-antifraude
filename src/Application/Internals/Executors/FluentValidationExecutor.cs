using Application.Internals.Adapters;
using FluentValidation;

/* ********************************************************************************************************
# * Copyright © 2026 Arify Labs - Todos los derechos reservados.
# *
# * Descripción                  : Ejecutor de validadores de FluentValidation
# *
# * Autor                    : Victor Jhampier Caxi Maquera
# * Correo/Móvil/Teléfono    : victorjhampier@gmail.com | 968991714
# *
# * Fecha de creación         : 01/01/2026
# *
# **********************************************************************************************************/

namespace Application.Internals.Executors;

internal static class FluentValidationExecutor
{
    public static IReadOnlyCollection<ValidationResultAdapter> Execute<T>(T input, IValidator<T> validator)
    {
        var validResult = validator.Validate(input);

        if (validResult.IsValid) return Array.Empty<ValidationResultAdapter>();

        return validResult.Errors
            .Select(x => new ValidationResultAdapter
            {
                Code = x.ErrorCode,
                Message = x.ErrorMessage,
                Field = x.PropertyName
            })
            .ToList();
    }
}
