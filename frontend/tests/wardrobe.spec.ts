import { Buffer } from 'node:buffer'
import { expect, test } from '@playwright/test'
import type { Page } from '@playwright/test'

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

/**
 * The header and the empty state both render a "Subir prenda" button, so the
 * locator is scoped with `.first()` only after the wardrobe has loaded.
 */
async function openUpload(page: Page) {
  await waitForWardrobe(page)
  await page.getByRole('button', { name: 'Subir prenda' }).first().click()
}

async function analyzeFixture(page: Page, fixture: string) {
  await openUpload(page)
  await page.setInputFiles('input[type=file]', `tests/fixtures/${fixture}`)
  await expect(page.getByLabel('Nombre')).toBeVisible()
}

test('sube una prenda y la ve en el guardarropa', async ({ page }) => {
  await page.goto('/guardarropa')
  await analyzeFixture(page, 'camisa.jpg')

  await page.getByLabel('Nombre').fill('Camisa de lino')
  await page.getByRole('button', { name: 'Guardar' }).click()

  // The dialog only closes after confirm succeeds, so this guards against a
  // false pass where the card behind the modal already shows the new garment.
  await expect(page.getByRole('dialog')).toBeHidden()
  await expect(page.getByText('Camisa de lino')).toBeVisible()
})

test('guarda las correcciones del formulario de confirmación', async ({
  page,
}) => {
  await page.goto('/guardarropa')
  await analyzeFixture(page, 'camisa-azul.jpg')

  await page.getByLabel('Nombre').fill('Camisa de lino azul')
  await page.getByRole('dialog').getByLabel('Color', { exact: true }).fill('celeste')
  await page.getByLabel('Color (hex)').fill('#123456')
  await page.getByRole('button', { name: 'Guardar' }).click()
  await expect(page.getByRole('dialog')).toBeHidden()

  await page.reload()
  await waitForWardrobe(page)

  const card = page
    .getByRole('article')
    .filter({ hasText: 'Camisa de lino azul' })
  await expect(card).toBeVisible()
  await expect(card.getByText('celeste')).toBeVisible()

  const swatch = card.locator('span[aria-hidden="true"]').first()
  await expect(swatch).toHaveCSS('background-color', 'rgb(18, 52, 86)')

  // The color hex must survive the confirm request, not only the name.
  await expect(card.getByText('Top')).toBeVisible()
})

test('re-subir una foto confirmada conserva el color y el nombre editado', async ({
  page,
}) => {
  await page.goto('/guardarropa')
  await analyzeFixture(page, 'camisa-hex.jpg')

  await page.getByLabel('Nombre').fill('Camisa con hex')
  await page.getByRole('dialog').getByLabel('Color', { exact: true }).fill('verde')
  await page.getByLabel('Color (hex)').fill('#00aa55')
  await page.getByRole('button', { name: 'Guardar' }).click()
  await expect(page.getByRole('dialog')).toBeHidden()

  // The same photo is re-uploaded: the form must open with the current columns,
  // not with the original analyze snapshot.
  await analyzeFixture(page, 'camisa-hex.jpg')
  await expect(page.getByLabel('Nombre')).toHaveValue('Camisa con hex')
  await expect(page.getByLabel('Color (hex)')).toHaveValue('#00aa55')

  await page.getByLabel('Nombre').fill('Camisa con hex editada')
  await page.getByRole('button', { name: 'Guardar' }).click()
  await expect(page.getByRole('dialog')).toBeHidden()

  await page.reload()
  await waitForWardrobe(page)

  const card = page
    .getByRole('article')
    .filter({ hasText: 'Camisa con hex editada' })
  await expect(card).toBeVisible()
  await expect(card.getByText('verde')).toBeVisible()
  const swatch = card.locator('span[aria-hidden="true"]').first()
  await expect(swatch).toHaveCSS('background-color', 'rgb(0, 170, 85)')
})

test('reintentar vuelve a llenar el formulario sin guardar', async ({
  page,
}) => {
  await page.goto('/guardarropa')
  await analyzeFixture(page, 'camisa-verde.jpg')

  await page.getByLabel('Nombre').fill('Nombre guardado')
  await page.getByRole('button', { name: 'Guardar' }).click()
  await expect(page.getByRole('dialog')).toBeHidden()

  await analyzeFixture(page, 'camisa-verde.jpg')
  await page.getByLabel('Nombre').fill('Nombre sin guardar')
  await page.getByRole('button', { name: 'Reintentar' }).click()

  // The fake analyzer always proposes "Camisa de lino"; it refills the form but
  // must not persist anything until the user presses Guardar.
  await expect(page.getByLabel('Nombre')).toHaveValue('Camisa de lino')

  await page.getByRole('button', { name: 'Cerrar' }).click()
  await expect(page.getByRole('dialog')).toBeHidden()
  await page.reload()
  await waitForWardrobe(page)
  await expect(page.getByText('Nombre guardado')).toBeVisible()
  await expect(page.getByText('Nombre sin guardar')).toBeHidden()
})

test('cancelar después de analizar no deja una fila en la base', async ({
  page,
  request,
}) => {
  const before = await request.get(`${API_URL}/api/garments`)
  const beforeBody = (await before.json()) as { items: unknown[] }

  await page.goto('/guardarropa')
  await analyzeFixture(page, 'camisa-cancelar.jpg')
  await page.getByRole('button', { name: 'Cerrar' }).click()
  await expect(page.getByRole('dialog')).toBeHidden()

  await page.reload()
  await waitForWardrobe(page)

  const after = await request.get(`${API_URL}/api/garments`)
  const afterBody = (await after.json()) as { items: unknown[] }
  expect(afterBody.items.length).toBe(beforeBody.items.length)
})

test('muestra el spinner mientras analiza la prenda', async ({ page }) => {
  await page.goto('/guardarropa')
  await page.route('**/api/garments/analyze', async (route) => {
    await new Promise((resolve) => setTimeout(resolve, 750))
    await route.continue()
  })

  await openUpload(page)
  await page.setInputFiles('input[type=file]', 'tests/fixtures/camisa-spinner.jpg')

  await expect(page.getByText('Analizando prenda…')).toBeVisible()
  await expect(page.getByLabel('Nombre')).toBeVisible()
})

test('rechaza una imagen de más de 10 MB antes de subirla', async ({
  page,
}) => {
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
  await expect(page.getByLabel('Nombre')).toBeHidden()
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
    page.getByRole('button', { name: 'Subir prenda' }),
  ).toHaveCount(0)
  await expect(page.getByRole('note')).toContainText(
    'La demo pública es de solo lectura: no se pueden subir prendas.',
  )
})
