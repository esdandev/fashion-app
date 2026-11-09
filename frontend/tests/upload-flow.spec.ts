import { copyFileSync, mkdirSync, rmSync, statSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { expect, test } from '@playwright/test'

/**
 * Manual portfolio capture of the flagship flow: select a photo, fill the
 * garment attributes and save. It records a WebM plus a poster frame and is
 * skipped unless CAPTURE=1, so the normal suite and CI never run it.
 *
 * Usage (from `frontend/`):
 *
 *   CAPTURE=1 pnpm exec playwright test tests/upload-flow.spec.ts
 *   # PowerShell: $env:CAPTURE='1'; pnpm exec playwright test tests/upload-flow.spec.ts
 */

const repoDir = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..')
const screenshotsDir = join(repoDir, 'docs', 'screenshots')
const videoDir = join(repoDir, '.tmp', 'upload-flow-video')
const videoPath = join(screenshotsDir, 'upload-flow.webm')
const posterPath = join(screenshotsDir, 'upload-flow-poster.png')

test.skip(process.env.CAPTURE !== '1', 'captura manual (CAPTURE=1)')

test('graba el flujo subir -> completar -> guardar', async ({ browser }) => {
  test.setTimeout(300_000)

  mkdirSync(screenshotsDir, { recursive: true })
  rmSync(videoDir, { recursive: true, force: true })
  mkdirSync(videoDir, { recursive: true })

  const context = await browser.newContext({
    viewport: { width: 1280, height: 800 },
    recordVideo: { dir: videoDir, size: { width: 1280, height: 800 } },
    locale: 'es-AR',
  })
  const page = await context.newPage()
  const video = page.video()

  try {
    await page.goto('/guardarropa')

    // Either an existing wardrobe or the empty state is a valid starting point.
    await expect(
      page
        .getByText(/^\d+ prendas?/)
        .or(page.getByRole('heading', { name: /guardarropa.*vac/ })),
    ).toBeVisible()

    await page.waitForTimeout(700)
    await page.getByRole('button', { name: 'Subir prendas' }).first().click()
    await expect(page.getByRole('dialog')).toBeVisible()
    await page.waitForTimeout(500)

    await page.setInputFiles('input[type=file]', 'tests/fixtures/camisa.jpg')

    // Give the recording time to show the populated form instead of one frame.
    await expect(page.getByLabel('Nombre').first()).toBeVisible()
    await expect(page.getByRole('dialog')).toContainText('Subir prendas')
    await page.waitForTimeout(900)

    await page.getByLabel('Nombre').first().fill('Camisa de lino')
    const dialog = page.getByRole('dialog')
    await dialog.getByLabel('Color', { exact: true }).fill('beige')
    await dialog.getByLabel('Color (hex)').fill('#d8c3a5')
    await page.waitForTimeout(500)
    await page.screenshot({ path: posterPath, fullPage: false })
    await page.waitForTimeout(300)

    await page.getByRole('button', { name: 'Guardar todas' }).click()
    await expect(page.getByRole('dialog')).toBeHidden()
    await expect(page.getByText('Camisa de lino')).toBeVisible()
    await page.waitForTimeout(900)
  } finally {
    await page.close().catch(() => undefined)
    await context.close()
  }

  const recorded = await video?.path()
  if (!recorded) {
    throw new Error('Playwright no devolvió un archivo de video')
  }

  copyFileSync(recorded, videoPath)
  rmSync(videoDir, { recursive: true, force: true })

  expect(statSync(videoPath).size).toBeGreaterThan(10_000)
  expect(statSync(posterPath).size).toBeGreaterThan(1_000)
})
