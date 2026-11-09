import { Buffer } from 'node:buffer'
import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'
import { clearGarments } from './helpers/seed'

const API_URL = 'http://localhost:5210'

/** Waits for the wardrobe grid or empty state before interacting. */
async function waitForWardrobe(page: Page) {
  await expect(
    page
      .getByText(/^\d+ prendas?/)
      .or(page.getByRole('heading', { name: 'Tu guardarropa está vacío' }))
      .or(
        page.getByRole('heading', {
          name: 'No hay prendas con esos filtros',
        }),
      ),
  ).toBeVisible()
}

/** The header and the empty state both render the upload button. */
async function openUpload(page: Page) {
  await waitForWardrobe(page)
  await page.getByRole('button', { name: 'Subir prendas' }).first().click()
}

async function selectFixtures(page: Page, ...fixtures: string[]) {
  await openUpload(page)
  await page.setInputFiles(
    'input[type=file]',
    fixtures.map((fixture) => `tests/fixtures/${fixture}`),
  )
  await expect(page.getByLabel('Nombre').first()).toBeVisible()
}

async function saveAll(page: Page) {
  await page.getByRole('button', { name: 'Guardar todas' }).click()
  await expect(page.getByRole('dialog')).toBeHidden()
}

test.beforeEach(async ({ request }) => {
  await clearGarments(request)
})

test('sube una prenda con datos manuales y la ve en el guardarropa', async ({
  page,
}) => {
  await page.goto('/guardarropa')
  await selectFixtures(page, 'camisa.jpg')

  await page.getByLabel('Nombre').first().fill('Camisa de lino')
  const dialog = page.getByRole('dialog')
  await dialog.getByLabel('Color', { exact: true }).fill('beige')
  await dialog.getByLabel('Color (hex)').fill('#d8c3a5')
  await saveAll(page)

  await expect(page.getByText('Camisa de lino')).toBeVisible()
})

test('sube varias prendas en un solo lote', async ({ page }) => {
  const suffix = Date.now()
  const firstName = `Camisa lote ${suffix}`
  const secondName = `Pantalón lote ${suffix}`

  await page.goto('/guardarropa')
  await selectFixtures(page, 'camisa.jpg', 'pantalon-beige.webp')

  const dialog = page.getByRole('dialog')
  await dialog.getByLabel('Nombre').nth(0).fill(firstName)
  await dialog.getByLabel('Categoría').nth(0).selectOption('top')
  await dialog.getByLabel('Nombre').nth(1).fill(secondName)
  await dialog.getByLabel('Categoría').nth(1).selectOption('bottom')
  await saveAll(page)

  await expect(page.getByText(firstName)).toBeVisible()
  await expect(page.getByText(secondName)).toBeVisible()
})

test('guarda color y nombre manuales y los conserva al recargar', async ({
  page,
}) => {
  await page.goto('/guardarropa')
  await selectFixtures(page, 'camisa-azul.jpg')

  const dialog = page.getByRole('dialog')
  await dialog.getByLabel('Nombre').first().fill('Camisa de lino azul')
  await dialog.getByLabel('Color', { exact: true }).fill('celeste')
  await dialog.getByLabel('Color (hex)').fill('#123456')
  await saveAll(page)

  await page.reload()
  await waitForWardrobe(page)

  const card = page
    .getByRole('article')
    .filter({ hasText: 'Camisa de lino azul' })
  await expect(card).toBeVisible()
  await expect(card.getByText('celeste')).toBeVisible()

  const swatch = card.locator('span[aria-hidden="true"]').first()
  await expect(swatch).toHaveCSS('background-color', 'rgb(18, 52, 86)')
  await expect(card.getByText('Top')).toBeVisible()
})

test('cancelar después de elegir fotos no deja una fila en la base', async ({
  page,
  request,
}) => {
  const before = await request.get(`${API_URL}/api/garments`)
  const beforeBody = (await before.json()) as { items: unknown[] }

  await page.goto('/guardarropa')
  await selectFixtures(page, 'camisa-cancelar.jpg')
  await page
    .getByRole('dialog')
    .getByRole('button', { name: 'Cerrar', exact: true })
    .last()
    .click()
  await expect(page.getByRole('dialog')).toBeHidden()

  await page.reload()
  await waitForWardrobe(page)

  const after = await request.get(`${API_URL}/api/garments`)
  const afterBody = (await after.json()) as { items: unknown[] }
  expect(afterBody.items.length).toBe(beforeBody.items.length)
})

test('rechaza una imagen de más de 10 MB antes de subirla', async ({ page }) => {
  await page.goto('/guardarropa')
  await openUpload(page)
  await page.setInputFiles('input[type=file]', {
    name: 'demasiado-grande.jpg',
    mimeType: 'image/jpeg',
    buffer: Buffer.alloc(10 * 1024 * 1024 + 1),
  })

  await expect(
    page.getByText('La imagen no puede superar los 10 MB.'),
  ).toBeVisible()
  await expect(page.getByLabel('Nombre')).toHaveCount(0)
})

test('en modo demo oculta las acciones de escritura', async ({ page }) => {
  await page.route('**/api/config', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({ demo: true }),
    })
  })

  await page.goto('/guardarropa')
  await waitForWardrobe(page)

  await expect(
    page.getByRole('button', { name: 'Subir prendas' }),
  ).toHaveCount(0)
  await expect(page.getByRole('note')).toContainText(
    'La demo pública es de solo lectura: no se pueden subir prendas.',
  )
})

test('elimina una prenda del guardarropa', async ({ page }) => {
  const name = `Prenda a eliminar ${Date.now()}`
  await page.goto('/guardarropa')
  await selectFixtures(page, 'camisa-verde.jpg')

  await page.getByLabel('Nombre').first().fill(name)
  await saveAll(page)
  await expect(page.getByText(name)).toBeVisible()

  const card = page.locator('article').filter({ hasText: name })
  await card.getByRole('button', { name: 'Eliminar prenda' }).click()
  await card.getByRole('button', { name: 'Sí, eliminar' }).click()

  await expect(page.locator('article').filter({ hasText: name })).toHaveCount(0)
})
