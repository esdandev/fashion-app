import { spawn, spawnSync } from 'node:child_process'
import { existsSync, mkdirSync, rmSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

/**
 * Starts the API for the Playwright run with an isolated database and uploads
 * root. The data directory is deleted before every run, so the E2E can never
 * touch the development database in `data/`.
 */
const frontendDir = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const repoDir = resolve(frontendDir, '..')
const dataDir = join(repoDir, '.test-data')
const dbPath = join(dataDir, 'e2e.db')
const uploadsDir = join(dataDir, 'uploads')
const project = join(repoDir, 'backend', 'FashionApp.Api')
const dll = join(project, 'bin', 'Debug', 'net10.0', 'FashionApp.Api.dll')

// On Windows the PATH entry can be an npm `.cmd` shim that Node cannot spawn
// without a shell. Prefer the real host when it is installed in the default
// location so the child process is the server itself (and stops cleanly).
const dotnet =
  [
    process.env.DOTNET_ROOT && join(process.env.DOTNET_ROOT, 'dotnet.exe'),
    'C:/Program Files/dotnet/dotnet.exe',
  ].find((candidate) => candidate && existsSync(candidate)) ?? 'dotnet'

rmSync(dataDir, { recursive: true, force: true })
mkdirSync(dataDir, { recursive: true })

const build = spawnSync(dotnet, ['build', project, '--nologo', '-v', 'q'], {
  cwd: repoDir,
  stdio: 'inherit',
})
if (build.status !== 0) {
  process.exit(build.status ?? 1)
}

const child = spawn(dotnet, [dll, '--urls', 'http://localhost:5210'], {
  cwd: repoDir,
  env: {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: 'Development',
    ConnectionStrings__Default: `Data Source=${dbPath}`,
    Storage__Root: uploadsDir,
    Ai__Provider: 'fake',
    Demo__Enabled: 'false',
    App__Token: '',
  },
  stdio: 'inherit',
})

function stop(signal) {
  if (!child.killed) {
    child.kill(signal)
  }
}

process.on('SIGINT', () => stop('SIGINT'))
process.on('SIGTERM', () => stop('SIGTERM'))
process.on('exit', () => stop('SIGTERM'))

child.on('exit', (code) => process.exit(code ?? 0))
