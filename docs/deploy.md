# Deploy en Fly.io

> **Handoff de deploy.** Este documento deja el deploy listo para que lo ejecute
> el dueño de la cuenta. En el entorno de desarrollo de este repositorio **no
> hay `flyctl` ni credenciales de Fly.io**: acá no se creó ninguna app, no se
> subió ninguna imagen y no se ejecutó ningún deploy. Todos los comandos de abajo
> se corren desde tu máquina, autenticado con tu propia cuenta. Los pasos que sí
> se validaron en este entorno están listados en
> [Qué se validó](#qué-se-validó-y-qué-no).

La app se publica como **un solo contenedor**: la API .NET sirve la SPA compilada
desde `wwwroot` y la API desde el mismo origen (puerto interno `8080`). SQLite y
las imágenes viven en el volumen persistente `fashion_data` montado en `/data`.

`fly.toml` (raíz del repo) levanta la **demo pública**:

- región `gru`, una Machine de 512 MB (shared CPU);
- HTTPS forzado, sin auto-stop (`auto_stop_machines = false`) y una Machine
  siempre encendida (`min_machines_running = 1`);
- `DATA__ROOT=/data` y `DEMO__DATAROOT=/data/demo`;
- feeds fake, sin generación de digest al arrancar;
- solo lectura: las escrituras de la demo responden `403`.

---

## Requisitos

- Una cuenta en [fly.io](https://fly.io).
- `flyctl` instalado y autenticado.
- El repositorio con el `Dockerfile`, `docker-compose.yml` y `fly.toml` de este
  proyecto. El build de la imagen lo hace Fly.io en su builder remoto: no hace
  falta Docker local.

## 1. Instalar `flyctl` y entrar

Windows (PowerShell), con `winget`:

```powershell
winget install Fly-io.flyctl
```

Windows, con el instalador oficial:

```powershell
iwr https://fly.io/install.ps1 -useb | iex
```

macOS / Linux:

```bash
curl -L https://fly.io/install.sh | sh
```

Cerrá y abrí la terminal para que el `PATH` tome el binario, y verificá:

```bash
fly version
fly auth login
fly auth whoami
```

`fly auth login` abre el navegador (o pide un token) y guarda la sesión. Si el
`PATH` todavía no lo ve, en Windows el instalador suele dejarlo en
`%USERPROFILE%\.fly\bin`.

## 2. Crear la app

Desde la raíz del repo, con el `fly.toml` ya presente:

```bash
fly apps create fashion-app-demo
```

El nombre es **global** en Fly.io. Si `fashion-app-demo` ya está tomado, elegí
otro, cambiá `app = "..."` en `fly.toml` y reemplazá ese nombre en todos los
comandos de este documento (y en las URLs de verificación).

Alternativa: `fly launch --no-deploy` detecta el `Dockerfile`, pero puede
proponer sobrescribir `fly.toml`; si lo usás, revisá que el archivo quede igual
al del repo antes de seguir. Crear la app a mano con `fly apps create` evita esa
ambigüedad.

## 3. Crear el volumen

El volumen debe existir **antes** del primer deploy: la Machine monta
`fashion_data` en `/data` y no arranca sin él.

```bash
fly volumes create fashion_data --region gru --size 1
```

- `--size 1` son 1 GB; alcanza de sobra para la demo (SQLite + WebP).
- Es un volumen por Machine. El `fly.toml` define el tamaño de la VM, no la
  cantidad de Machines: un deploy nuevo crea una Machine en `gru` y el volumen
  se monta en ella. Si escalás a más Machines, cada una necesita un volumen en su
  región.
- No borres ni recrees el volumen para "resetear" la demo: eso borra también
  datos personales si alguna vez usaste la misma app para la instancia personal.
  El reset correcto está en [Reset de la demo](#7-reset-de-la-demo).

Comprobá que quedó creado:

```bash
fly volumes list
```

## 4. Deployar

```bash
fly deploy
```

Fly.io construye la imagen multi-stage desde el `Dockerfile` (Node 22 + pnpm
compilan la SPA, .NET 10 publica la API, la imagen final corre como el usuario
`app`), la sube a su registro y crea la Machine con el volumen montado.

Seguimiento:

```bash
fly status
fly logs
```

El deploy espera a que el health check `GET /api/health` responda `200`. Si la
Machine queda reiniciando, `fly logs` muestra el error de arranque (por ejemplo,
un volumen ausente o una ruta de datos inválida).

## 5. Abrir la app

```bash
fly open
```

Abre `https://fashion-app-demo.fly.dev` en el navegador. Al arrancar, la app
siembra la demo (14 prendas y un digest de 8 notas) en `/data/demo` antes de
empezar a escuchar, así que la primera visita ya la encuentra lista.

## 6. Verificación

Reemplazá `APP_URL` si usaste otro nombre de app.

```bash
APP_URL=https://fashion-app-demo.fly.dev

# 1) Salud del servicio: 200 con status "ok" y la hora UTC
curl -sS "$APP_URL/api/health"

# 2) La SPA se sirve con index.html no cacheable
curl -sS -D - -o /dev/null "$APP_URL/" | grep -i "cache-control"

# 3) Los assets con hash son inmutables
#    (copiá un nombre /assets/index-*.js del HTML y pedilo)
curl -sSI "$APP_URL/assets/index-XXXXXXXX.js" | grep -i "cache-control"

# 4) La config pública confirma el modo demo
curl -sS "$APP_URL/api/config"

# 5) El digest sembrado está disponible
curl -sS "$APP_URL/api/digest/today"
```

Resultados esperados:

| Pedido | Resultado |
|---|---|
| `GET /api/health` | `200` y `{"status":"ok","utc":"..."}` |
| `GET /` | `200`, `text/html`, `Cache-Control: no-cache` |
| `GET /assets/<hash>.js` | `200`, `Cache-Control: public, max-age=31536000, immutable` |
| `GET /api/config` | `200` y `{"demo":true}` |
| `GET /api/digest/today` | `200` con el digest de demo (8 ítems) y `ETag` |
| `POST /api/digest/refresh` | `403` (la demo es de solo lectura) |

En PowerShell, `curl` es un alias de `Invoke-WebRequest`: usá `curl.exe` para
que los flags funcionen igual.

```powershell
$APP_URL = "https://fashion-app-demo.fly.dev"
curl.exe -sS "$APP_URL/api/health"
curl.exe -sS -D - -o NUL "$APP_URL/" | Select-String -Pattern "cache-control"
curl.exe -sS "$APP_URL/api/config"
```

## 7. Reset de la demo

La demo se siembra sola al arrancar, cuando sus tablas están vacías. Para volver
al estado inicial hay que borrar **solo** `/data/demo` y reiniciar el proceso:

```bash
fly ssh console -a fashion-app-demo -C "rm -rf /data/demo"
fly apps restart fashion-app-demo
```

- `fly ssh console -C` ejecuta el comando sin abrir una sesión interactiva; si
  preferís una shell, entrá con `fly ssh console` y corré `rm -rf /data/demo`.
- El comando usa `/data/demo` porque `DEMO__DATAROOT=/data/demo` en `fly.toml`.
  Si cambiás `DEMO__DATAROOT`, borrá esa ruta en su lugar: siempre dentro de
  `/data` y nunca la raíz personal (el arranque falla si coinciden).
- Al reiniciar, `DemoSeeder` vuelve a crear la base y las imágenes desde el
  manifiesto.
- `fly apps restart` **solo** reinicia la Machine: no borra ni vuelve a sembrar
  datos. Es el paso posterior al borrado, no un reemplazo del borrado.
- **Nunca borres `/data` completo.** El volumen también contiene
  `/data/fashion.db` y `/data/uploads`, que son los datos de la instancia
  personal si alguna vez apagaste la demo en esta misma app.
- La demo no acepta escrituras (responden `403`), así que el estado sembrado no
  cambia con el uso normal. El reset solo hace falta si cambiaste el manifiesto
  o querés descartar el estado sembrado.

## 8. Instancia personal

La instancia personal usa la misma imagen pero **sin demo**, con token de acceso
y proveedores reales. Los secretos de Fly.io tienen prioridad sobre `[env]` de
`fly.toml`, así que alcanza con setearlos; no hace falta editar el archivo.

En la **misma app** (deja de ser la demo pública y pasa a ser la instancia
personal):

```bash
fly secrets set \
  APP__TOKEN='un-secreto-largo-y-aleatorio' \
  DEMO__ENABLED=false

fly deploy
```

- `APP__TOKEN` es obligatorio en `Production` si la demo está apagada: sin él, el
  contenedor se niega a arrancar (por diseño). Es un secreto compartido, no
  autenticación real.
- Los secretos de Fly.io **pisan** las variables de `[env]` con el mismo nombre.
  En PowerShell, `fly secrets set` no acepta la continuación con `\`: poné todos
  los pares `CLAVE=valor` en una sola línea.
- En la UI, guardá el mismo token en `sessionStorage` bajo la clave
  `app_token`; el cliente lo manda como `X-App-Token`. En la consola del
  navegador del dominio de la app:

  ```js
  sessionStorage.setItem('app_token', 'un-secreto-largo-y-aleatorio')
  ```

  Recargá la página. El valor vive en esa pestaña y se pierde al cerrarla.
- `fly secrets set` ya reinicia las Machines. El `fly deploy` de arriba solo hace
  falta si además cambió la imagen; no está de más para dejar la release
  explícita.
- Los datos personales quedan en `/data/fashion.db` y `/data/uploads`; la demo
  queda intacta en `/data/demo` y no se toca al apagar `DEMO__ENABLED`.

Para el digest real, agregá los feeds y el secreto administrativo (opcional,
pero sin esto el digest sigue siendo fake o queda vacío):

```bash
fly secrets set \
  DIGEST__PROVIDER=rss \
  DIGEST__FEEDS__0__NAME='Fuente' \
  DIGEST__FEEDS__0__URL='https://ejemplo.com/feed.xml' \
  DIGEST__ADMINSECRET='otro-secreto-largo'
```

`DIGEST__GENERATEONSTARTUP=false` sigue viniendo de `fly.toml`; si querés que el
digest se genere al arrancar, setealo también como secreto en `true`.

### Demo y personal al mismo tiempo

Una app tiene un solo conjunto de variables de entorno. Si querés mantener la
demo pública y la instancia personal online a la vez, creá una segunda app con
su propio volumen:

```bash
fly apps create fashion-app-personal
fly volumes create fashion_data --region gru --size 1 --app fashion-app-personal
```

Copiá `fly.toml` a `fly.personal.toml` y cambiá `app = "fashion-app-personal"`.
Dejá `DEMO__ENABLED = "false"` en `[env]` y aplicá los secretos personales
**antes del primer deploy**: con la demo apagada, el contenedor no arranca en
`Production` sin `APP__TOKEN`, así que la primera release tiene que encontrarlo
ya configurado.

```bash
fly secrets set --app fashion-app-personal \
  APP__TOKEN='un-secreto-largo-y-aleatorio'

fly deploy --config fly.personal.toml
```

Los feeds del digest y `DIGEST__ADMINSECRET` se agregan con el mismo `--app`
cuando los configures:

```bash
fly secrets set --app fashion-app-personal \
  DIGEST__PROVIDER=rss \
  DIGEST__FEEDS__0__NAME='Fuente' \
  DIGEST__FEEDS__0__URL='https://ejemplo.com/feed.xml' \
  DIGEST__ADMINSECRET='otro-secreto-largo'
```

`fly.personal.toml` está en `.gitignore`: es configuración local y los secretos
van siempre por `fly secrets set`.

## 9. Costos, límites y decisiones

- La demo mantiene **una Machine siempre encendida** (`auto_stop_machines =
  false`, `min_machines_running = 1`) para que la primera visita no pague el
  arranque en frío. Si preferís ahorrar, cambiá a `auto_stop_machines = "stop"` y
  `min_machines_running = 0`; la primera request tarda unos segundos.
- 512 MB alcanza para ASP.NET + SQLite + Magick.NET. La imagen instala `libgomp1`
  porque Magick.NET lo necesita en Linux.
- El volumen vive en `gru`; no hay réplicas ni multi-región. Un deploy reinicia
  la Machine pero no borra `/data`.
- La demo no requiere token y es de solo lectura; la instancia personal sí exige
  `APP__TOKEN`. No expongas la instancia personal sin token.
- **La Machine solo debe ser accesible a través del proxy de Fly.** El rate
  limit resuelve la IP del visitante con `Fly-Client-IP`, después con el primer
  valor de `X-Forwarded-For` y por último con la dirección del socket. Esos
  headers se pueden falsificar en una conexión directa, así que no publiques la
  Machine por una IP pública ni desactives el proxy: el límite por IP solo es
  confiable cuando el único camino de entrada es el edge de Fly.
- El deploy es manual. El workflow de CI (`.github/workflows/ci.yml`) no
  deploya ni usa secretos reales.

## Qué se validó y qué no

Validado en este entorno (sin credenciales):

- sintaxis TOML de `fly.toml` y que todas sus claves (`internal_port`,
  `force_https`, `mounts`, `env`, `vm`, health check) existen en la referencia
  de configuración de Fly.io;
- que cada variable de entorno usada existe en `appsettings.json` /
  `appsettings.Development.json` y en el código (`Demo:DataRoot`,
  `App:AllowAnonymous`, `Digest:GenerateOnStartup`, etc.);
- que el `Dockerfile` escucha en `8080`, usa `/data` y corre como `app`;
- que los comandos documentados existen en `flyctl` (`apps create`,
  `volumes create`, `deploy`, `open`, `ssh console -C`, `apps restart`,
  `secrets set`, `volumes list`, `status`, `logs`) y que los endpoints de
  verificación existen en la API.

**No validado acá:** `fly deploy`, la creación real de app/volumen, el build de
la imagen en el builder de Fly.io y las respuestas públicas. Eso requiere tu
cuenta y es el handoff de deploy de la Fase 4.
