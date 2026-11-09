# Fashion App — Plan de implementación: Fase 4 (portfolio y deploy)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dejar la app lista para publicar como portfolio: un solo contenedor que sirve API + SPA, configuración de deploy con volumen persistente, demo aislada, CI, capturas y README final.

**Architecture:** En producción, ASP.NET sirve la SPA compilada desde `wwwroot` y la API desde el mismo origen, con un volumen para SQLite e imágenes. El deploy queda preparado para Fly.io; la demo corre con `Demo:Enabled=true`, datos sembrados, IA fake y sin escritura. El deploy real lo ejecuta el dueño con su cuenta (no hay credenciales en este entorno).

**Tech Stack:** .NET 10, React 19 + Vite, Docker multi-stage, Fly.io, GitHub Actions, Playwright, Lighthouse.

**Spec:** `00 - Projects/Fashion App/Fashion App.md`

## Índice del plan

- [[#Global Constraints]]
- [[#Review Focus]]
- [[#File Structure]]
- [[#Task 1: SPA servida por la API y caché de estáticos]]
- [[#Task 2: Imagen Docker y compose local]]
- [[#Task 3: Configuración de Fly.io y handoff de deploy]]
- [[#Task 4: CI de GitHub Actions]]
- [[#Task 5: Capturas, README de portfolio y métricas]]
- [[#Task 6: Verificación final y cierre]]

## Global Constraints

- .NET 10, React 19, Vite, Tailwind 4; UI y errores en español.
- Un solo origen en producción: la API sirve `/` y los assets; `/api/*` y `/media/*` mantienen prioridad.
- `wwwroot` generado en el build de Docker; **no** se commitea.
- Datos persistentes en `DATA__ROOT` (volumen `/data`); SQLite y uploads derivan de ahí.
- Demo: `DEMO__ENABLED=true`, `DEMO__DATAROOT=/data/demo`, `AI__PROVIDER=fake`, `DIGEST__PROVIDER=fake`, `DIGEST__GENERATEONSTARTUP=false`; sin escritura y sin IA.
- Instancia personal: `APP__TOKEN` obligatorio en Production, IA real, demo apagada.
- Assets con hash: `Cache-Control: public, max-age=31536000, immutable`; `index.html` con `no-cache`.
- El deploy real no se ejecuta en este entorno; se documenta y se valida todo lo que no requiere credenciales.
- CI: backend, frontend y E2E en cada push/PR; sin secretos reales.

## Review Focus

1. **SPA y API en el mismo origen** → `/api/health` y `/media/...` nunca caen en el fallback de `index.html`; una ruta desconocida de la SPA sí.
2. **Datos efímeros en el contenedor** → SQLite e imágenes viven en `/data`; un redeploy no borra la demo.
3. **Demo que apunta a datos personales** → el arranque falla si `DEMO__DATAROOT` coincide con la raíz personal.
4. **Caché de `index.html`** → nunca immutable; un deploy nuevo se ve sin purgar CDN.
5. **CI sin secretos** → los tests usan fake providers y una base temporal; el workflow no requiere claves.

---

## File Structure

```
Dockerfile
.dockerignore
docker-compose.yml
fly.toml
.github/workflows/ci.yml
docs/screenshots/
frontend/src/test-backend.mjs (o tests/test-backend.mjs)
```

---

### Task 1: SPA servida por la API y caché de estáticos

**Files:**
- Modify: `backend/FashionApp.Api/Program.cs`, `backend/FashionApp.Tests/SpaHostingTests.cs` (nuevo)
- Create: `backend/FashionApp.Api/Infrastructure/StaticFiles.cs`

**Interfaces:**
- Consumes: `Program`, `TestAppFactory`.
- Produces: en producción, `GET /` sirve `wwwroot/index.html`; `/api/*` y `/media/*` siguen funcionando; assets con hash y caché immutable; `index.html` con `no-cache`.

- [ ] **Step 1: Escribir el test que falla**

```csharp
[Fact]
public async Task Root_serves_the_spa_when_wwwroot_exists()
{
    using var factory = new TestAppFactory { WithSpa = true };
    var client = factory.CreateClient();
    var response = await client.GetAsync("/");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Contains("text/html", response.Content.Headers.ContentType!.ToString());
    Assert.Contains("Fashion App", await response.Content.ReadAsStringAsync());
}

[Fact]
public async Task Unknown_api_route_is_not_swallowed_by_the_spa()
{
    using var factory = new TestAppFactory { WithSpa = true };
    var response = await factory.CreateClient().GetAsync("/api/no-existe");
    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    Assert.DoesNotContain("text/html", response.Content.Headers.ContentType?.ToString() ?? "");
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test backend/FashionApp.sln --filter "FullyQualifiedName~SpaHostingTests"`
Expected: FAIL.

- [ ] **Step 3: Implementar el hosting de estáticos**

En `Program.cs`, después de mapear `/api/*`, `/media/*` y antes de `app.Run()`:

```csharp
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var name = ctx.File.Name;
        ctx.Context.Response.Headers.CacheControl =
            name.Equals("index.html", StringComparison.OrdinalIgnoreCase)
                ? "no-cache"
                : "public, max-age=31536000, immutable";
    }
});
app.MapFallbackToFile("index.html");
```

- [ ] **Step 4: Soportar `WithSpa` en tests**

`TestAppFactory` crea un `wwwroot/index.html` temporal con `<title>Fashion App</title>` y ajusta `WebRootPath`; por defecto no lo crea.

- [ ] **Step 5: Correr los tests y la suite**

Run: `dotnet test backend/FashionApp.sln --filter "FullyQualifiedName~SpaHostingTests"`
Expected: PASS. Luego la suite completa.

- [ ] **Step 6: Commit**

```bash
git add backend
git commit -m "feat: serve the SPA from the API in production"
```

---

### Task 2: Imagen Docker y compose local

**Files:**
- Create: `Dockerfile`, `.dockerignore`, `docker-compose.yml`

**Interfaces:**
- Consumes: `backend/FashionApp.sln`, `frontend/package.json`.
- Produces: imagen multi-stage que compila frontend + backend, copia `dist` a `wwwroot`, corre como usuario no root con `/data` persistente y expone `8080`.

- [ ] **Step 1: Escribir el Dockerfile**

Etapas:
1. `node:22-alpine` → `corepack enable` + `pnpm install --frozen-lockfile` + `pnpm build`.
2. `mcr.microsoft.com/dotnet/sdk:10.0` → `dotnet restore` + `dotnet publish -c Release -o /app/publish`.
3. `mcr.microsoft.com/dotnet/aspnet:10.0` → publicar, copiar `dist` a `wwwroot`, `ENV DATA__ROOT=/data`, `ASPNETCORE_URLS=http://+:8080`, `EXPOSE 8080`, usuario `app`, `ENTRYPOINT ["dotnet","FashionApp.Api.dll"]`.

`.dockerignore`: `**/node_modules`, `**/bin`, `**/obj`, `data`, `docs/screenshots`, `.git`, `.superpowers`, `frontend/dist`.

- [ ] **Step 2: Escribir `docker-compose.yml`**

Servicio `app` con `build: .`, `ports: 8080:8080`, volumen `fashion_data:/data`, `DEMO__ENABLED=true`, `DEMO__DATAROOT=/data/demo`, `AI__PROVIDER=fake`, `DIGEST__PROVIDER=fake`, `DIGEST__GENERATEONSTARTUP=false`. Healthcheck `wget -qO- http://localhost:8080/api/health`.

- [ ] **Step 3: Validar lo que no requiere Docker**

```bash
pnpm --dir frontend build
dotnet publish backend/FashionApp.Api -c Release -o .tmp/publish
```

Verificar que `.tmp/publish/FashionApp.Api.dll` existe y que el build de frontend produce `frontend/dist/index.html`.

- [ ] **Step 4: Documentar que el build de imagen queda para el handoff**

No hay Docker en este entorno; anotarlo en el reporte y en README. No inventar una verificación que no se corrió.

- [ ] **Step 5: Commit**

```bash
git add Dockerfile .dockerignore docker-compose.yml README.md
git commit -m "chore: add production container and compose setup"
```

---

### Task 3: Configuración de Fly.io y handoff de deploy

**Files:**
- Create: `fly.toml`, `docs/deploy.md`
- Modify: `README.md`

**Interfaces:**
- Consumes: imagen Docker, `DATA__ROOT`, demo config.
- Produces: `fly.toml` con volumen `/data`, healthcheck, `force_https`; `docs/deploy.md` con comandos exactos para crear app, volumen, secretos y deployar; sección de reset de demo.

- [ ] **Step 1: Escribir `fly.toml`**

```toml
app = "fashion-app-demo"
primary_region = "gru"

[build]

[http_service]
  internal_port = 8080
  force_https = true
  auto_stop_machines = false
  min_machines_running = 1

[[mounts]]
  source = "fashion_data"
  destination = "/data"

[env]
  ASPNETCORE_ENVIRONMENT = "Production"
  DATA__ROOT = "/data"
  DEMO__ENABLED = "true"
  DEMO__DATAROOT = "/data/demo"
  AI__PROVIDER = "fake"
  DIGEST__PROVIDER = "fake"
  DIGEST__GENERATEONSTARTUP = "false"
  APP__ALLOWANONYMOUS = "false"

[[vm]]
  memory = "512mb"
  cpu_kind = "shared"
  cpus = 1
```

- [ ] **Step 2: Escribir `docs/deploy.md`**

Incluir:
1. `winget install Fly.cli` o el instalador oficial; `fly auth login`.
2. `fly apps create fashion-app-demo` (o `fly launch --no-deploy`).
3. `fly volumes create fashion_data --region gru --size 1`.
4. `fly deploy`.
5. `fly open`.
6. Persona: `fly secrets set APP__TOKEN=... AI__PROVIDER=gemini AI__APIKEY=... DEMO__ENABLED=false` y `fly deploy`.
7. Reset de demo: `fly ssh console` + borrar `/data/demo`, o `fly apps restart`; nunca borrar `/data` personal.
8. Verificación: `curl https://<app>.fly.dev/api/health`, `curl -I /` (no-cache), `/api/digest/today`.

- [ ] **Step 3: Documentar límites**

Sin credenciales ni `flyctl` en este entorno, el deploy real queda a cargo del dueño. El plan no promete un deploy ejecutado.

- [ ] **Step 4: Commit**

```bash
git add fly.toml docs/deploy.md README.md
git commit -m "docs: add Fly.io deployment configuration and handoff"
```

---

### Task 4: CI de GitHub Actions

**Files:**
- Create: `.github/workflows/ci.yml`

**Interfaces:**
- Consumes: backend/frontend tests; fake providers.
- Produces: workflow en push/PR con jobs `backend`, `frontend`, `e2e`; cache de NuGet y pnpm; sin secretos.

- [ ] **Step 1: Escribir el workflow**

- `backend`: `actions/setup-dotnet@v4` con `10.0.x`, `dotnet restore`, `dotnet test --no-restore`.
- `frontend`: `pnpm/action-setup`, `actions/setup-node@v4` con cache pnpm, `pnpm install --frozen-lockfile`, `pnpm vitest run`, `pnpm lint`, `pnpm build`.
- `e2e`: depends on backend/frontend; instala Playwright Chromium con `--with-deps`, corre `pnpm exec playwright test`.
- `concurrency` para cancelar runs viejos; `permissions: contents: read`.

- [ ] **Step 2: Validar el YAML**

No hay remoto ni Actions local; validar sintaxis con `python -c "import yaml,sys; yaml.safe_load(open('.github/workflows/ci.yml'))"` o `npx yaml-lint`, y revisar que los comandos existan.

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: add backend, frontend and e2e workflow"
```

---

### Task 5: Capturas, README de portfolio y métricas

**Files:**
- Create: `frontend/tests/screenshots.spec.ts`, `docs/screenshots/.gitkeep`, `docs/architecture.md`
- Modify: `README.md`

**Interfaces:**
- Consumes: demo data (wardrobe + digest), Playwright.
- Produces: capturas desktop/mobile de Hoy, Guardarropa y Outfits; README con arquitectura, features, screenshots, stack, cómo correr, deploy y decisiones; tabla de métricas.

- [ ] **Step 1: Escribir el spec de capturas**

`test.skip(process.env.CAPTURE !== '1', 'capturas manuales')`; si `CAPTURE=1`, siembra prendas + digest vía API, navega a `/`, `/guardarropa`, `/outfits`, y guarda PNG en `../docs/screenshots` con viewport desktop (1440×900) y mobile (390×844). No corre en CI normal.

- [ ] **Step 2: Capturar**

```bash
cd frontend && CAPTURE=1 pnpm exec playwright test tests/screenshots.spec.ts
```

Verificar que existan al menos 6 PNG y que no estén vacíos.

- [ ] **Step 3: Escribir `docs/architecture.md`**

Diagrama Mermaid: navegador → API .NET → SQLite/uploads; jobs de RSS/IA; demo vs personal. Decisiones clave enlazadas al spec.

- [ ] **Step 4: README final**

Secciones: qué es, capturas, features, stack, arquitectura, cómo correr local, tests, variables de entorno, deploy (link a `docs/deploy.md`), límites conocidos, estructura del repo.

- [ ] **Step 5: Medir**

Servir el build de producción localmente (`pnpm build`, copiar `dist` a `wwwroot`, `dotnet run -c Release`) con demo sembrada y correr Lighthouse mobile sobre `/`. Registrar LCP, INP/CLS y tamaño del bundle en una tabla del README. Si Lighthouse no está disponible, registrar el bundle real y decir qué no se midió.

- [ ] **Step 6: Commit**

```bash
git add frontend docs README.md
git commit -m "docs: add portfolio screenshots, architecture and README"
```

---

### Task 6: Verificación final y cierre

**Files:**
- Modify: `docs/Fashion App.md`, `docs/plan-fase-4.md`
- Create: `docs/smoke.md` (checklist)

- [ ] **Step 1: Correr todo**

`dotnet test backend/FashionApp.sln`, `pnpm vitest run`, `pnpm lint`, `pnpm build`, `pnpm exec playwright test`.

- [ ] **Step 2: Smoke de producción local**

Con `frontend/dist` copiado a `wwwroot` y `DEMO__ENABLED=true`:
- `GET /` sirve HTML de la SPA;
- `/api/health` 200;
- `/api/digest/today` con 8 ítems de demo;
- `/guardarropa` (fallback) sirve la SPA;
- `/media/...` sirve WebP;
- una escritura en demo devuelve 403.

- [ ] **Step 3: Documentar**

Actualizar bitácora y roadmap del spec (Fase 4 cerrada), `docs/smoke.md` con el checklist y resultados, y `README.md` con el estado final.

- [ ] **Step 4: Commit y verificación**

```bash
git add docs README.md
git commit -m "docs: close phase 4 with smoke checklist and final status"
```

---

## Nota de handoff

El deploy público requiere cuenta de Fly.io (o el host que elijas), un volumen persistente y, para la instancia personal, secretos de IA y token. Nada de eso se ejecuta desde este entorno. La fase deja el contenedor, la configuración, el CI y la documentación listos para que el deploy sea un puñado de comandos.

> **Estado:** Fase 4 implementada y verificada localmente el 2026-11-09 (265 tests backend, 51 frontend, 11 E2E). El deploy público y el build de la imagen quedan como handoff del dueño.
