# Validación de la copia independiente

Fecha: 24/10/2026. Destino: `diazm005/transferencias-api-antifraude`.

Esta entrega reúne las dependencias de la etapa anterior dentro del mismo repositorio.
Los proyectos no enlazan archivos ni bibliotecas desde el repositorio anterior.
Las implementaciones de negocio se conservan; se ajustaron rutas, arranques y pruebas
y se excluyeron ejemplos no utilizados de la plantilla original.

Comprobaciones reproducibles mediante `./scripts/validar.ps1`:

| Grupo | Comprobaciones |
|---|---:|
| Negocio e idempotencia | 41 |
| Modelo EF frente a DDL | 191 |
| Cliente HTTP con respuestas controladas | 16 |
| JWT/API y batch HTTP local real | 103 |
| Batch CLI: entrada válida e inválida | 2 |

Las pruebas predeterminadas no acceden a SQL. El proyecto `TestDoubles` contiene los
simuladores de almacenamiento que usan las pruebas; la API no referencia ese proyecto.

La integración SQL completa fue verificada en la etapa de origen con 17 comprobaciones
y limpieza de sus datos ficticios. Este traslado no requiere repetir escrituras en
la BD compartida. El comando `--sql-integration` queda disponible para el equipo;
no forma parte de CI y requiere conexión local explícita.

La clave JWT se obtiene desde User Secrets del usuario; no forma parte de la copia.
Se revisan los archivos a publicar para excluir credenciales, tokens, cadenas de
conexión reales y archivos de base de datos. Los scripts SQL contienen únicamente
estructura, documentación y ejemplos ficticios.
