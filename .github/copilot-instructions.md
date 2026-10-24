# Convenciones del proyecto

- Comentarios y documentación en español. Preservar los avisos legales existentes.
- Domain no depende de Application, EF ni de los ejecutables.
- Application coordina mediante interfaces; los detalles HTTP y EF pertenecen a infraestructura.
- Fraude.Batch es independiente y no referencia otros proyectos.
- Solo BD_Equipo2 está autorizada. No aplicar migraciones ni cargar datos automáticamente al arrancar.
- No guardar credenciales, claves de firma, tokens ni datos privados en el repositorio.
- Ejecutar `./scripts/validar.ps1` para las comprobaciones sin SQL; las pruebas SQL requieren configuración local explícita.
