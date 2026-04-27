# Copilot Instructions

## Directrices del proyecto
- El usuario prefiere el formato de comentarios `// === TITLE ===` en el código.
- El usuario prefiere español como idioma predeterminado y prioritario en las respuestas del agente personalizado.

## Eficiencia de tokens
- Responder de forma breve, directa y accionable.
- Evitar explicaciones largas salvo que el usuario las pida explícitamente.
- Evitar repetir contexto ya mencionado.
- Priorizar listas cortas y pasos mínimos.
- Proponer solo una opción por defecto; ofrecer alternativas solo si son necesarias.

## Contexto técnico del proyecto
- Proyecto vinculado a Unity 6.4.
- Priorizar recomendaciones compatibles con Unity 6.4 y C# usado en Unity.
- Favorecer soluciones simples, mantenibles y de bajo impacto en iteración del editor.

## Flujo de validación y compilación
- No ejecutar compilaciones del proyecto para “verificar” cambios por defecto.
- No sugerir compilar como paso automático tras cada cambio.
- Asumir que Unity Editor recompila automáticamente scripts al detectar cambios.
- Solo indicar compilación/verificación manual si el usuario la solicita explícitamente.

## Edición de código
- Hacer cambios mínimos y enfocados al objetivo solicitado.
- Mantener estilo y patrones existentes del código.
- No introducir refactors amplios no solicitados.
- No usar la terminal para editar o crear archivos. Usar solo las herramientas `create_file` y `edit` para eso.
- No borrar y recrear archivos para editar; editar siempre sobre el archivo existente. Mantener el mismo formato de código que usa el usuario en el proyecto.
- En estructuras `if/else` con una sola línea, no usar llaves; y si es corto (declaración o return), mantenerlo en la misma línea.