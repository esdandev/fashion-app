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

## Scripts

- `pnpm dev` — servidor de desarrollo.
- `pnpm test` — tests unitarios con Vitest.
- `pnpm build` — typecheck y build de producción.
- `pnpm lint` — oxlint.
