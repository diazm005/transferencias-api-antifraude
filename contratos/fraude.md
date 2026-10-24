# Contrato HTTP del evaluador independiente

`POST http://127.0.0.1:5310/api/fraud/evaluations`

Entrada obligatoria:

```json
{
  "transactionReference": "11111111-1111-4111-8111-111111111111",
  "amount": 10000,
  "previousDebitsIn15Minutes": 0,
  "knownDestination": false
}
```

Salida HTTP 200:

```json
{
  "transactionReference": "11111111-1111-4111-8111-111111111111",
  "riskLevel": "Medio",
  "reason": "El monto es igual o mayor que S/ 10 000.",
  "ruleVersion": "TALLER-1.0"
}
```

La referencia debe ser un GUID distinto de cero; el monto, positivo, de hasta dos
decimales y compatible con decimal(18,2). El número de débitos previos es entero no
negativo. `knownDestination` debe estar presente incluso si es `false`.

Se aplica la primera condición verdadera:

| Condición | Resultado |
|---|---|
| Monto ≥ 40 000 o débitos previos ≥ 5 | Alto |
| Monto ≥ 10 000 | Medio |
| Destino nuevo y monto ≥ 5 000 | Medio |
| Resto | Bajo |

Una entrada inválida devuelve HTTP 400. La API valida que la referencia coincida,
que el riesgo sea reconocido y que la versión y el motivo sean válidos. El cliente
HTTP tiene un timeout de dos segundos; no reenvía automáticamente la solicitud.

El proveedor conserva la versión `TALLER-1.0` para cumplir el contrato existente,
pero es dueño de su implementación. No depende de las DLL del servicio bancario.
La API adapta la respuesta a `NivelRiesgo` exclusivamente desde infraestructura.

El modo CLI `--once` utiliza exactamente esta entrada y salida; no requiere JWT ni
SQL. El endpoint HTTP es interno de desarrollo. El JWT del cliente de transferencias
no se envía al evaluador.
