# Fashion App — frontend

SPA en Vite + React + TypeScript para el guardarropa personal.

## Desarrollo

```bash
pnpm install
pnpm dev
```

La app corre en `http://localhost:5173` y consume la API en
`http://localhost:5210` por defecto. Para apuntar a otro backend definí
`VITE_API_URL`.

En la instancia personal, guardá el token en `sessionStorage` con la clave
`app_token`; el cliente lo envía como `X-App-Token`.

El guardarropa pagina de a 24 prendas con un botón "Cargar más", filtra por
categoría, color (con sugerencias de los colores ya cargados) y temporada, y el
formulario de confirmación usa un selector acotado de etiquetas (máximo 4).

## Scripts

- `pnpm dev` — servidor de desarrollo.
- `pnpm test` — tests unitarios con Vitest.
- `pnpm test:e2e` — flujo crítico con Playwright. Levanta Vite y un backend
  fake aislado (`.test-data/` en la raíz del repo) con la base de test limpia.
- `pnpm build` — typecheck y build de producción.
- `pnpm lint` — oxlint.

La primera vez hay que instalar el navegador:
`pnpm exec playwright install chromium`.
