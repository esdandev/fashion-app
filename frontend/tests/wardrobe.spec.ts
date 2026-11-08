import { Buffer } from 'node:buffer'
import { expect, test } from '@playwright/test'

test('sube una prenda y la ve en el guardarropa', async ({ page }) => {
  await page.goto('/guardarropa')
  await page.getByRole('button', { name: 'Subir prenda' }).click()
  await page.setInputFiles('input[type=file]', 'tests/fixtures/camisa.jpg')
  await expect(page.getByLabel('Nombre')).toBeVisible()
  await page.getByLabel('Nombre').fill('Camisa de lino')
  await page.getByRole('button', { name: 'Guardar' }).click()
  // The dialog only closes after the PATCH succeeds, so this guards against a
  // false pass where the card behind the modal already shows the new garment.
  await expect(page.getByRole('dialog')).toBeHidden()
  await expect(page.getByText('Camisa de lino')).toBeVisible()
})

test('guarda las correcciones del formulario de confirmación', async ({
  page,
}) => {
  await page.goto('/guardarropa')
  await page.getByRole('button', { name: 'Subir prenda' }).click()
  await page.setInputFiles('input[type=file]', 'tests/fixtures/camisa.jpg')
  await expect(page.getByLabel('Nombre')).toBeVisible()
  await page.getByLabel('Nombre').fill('Camisa de lino azul')
  await page.getByLabel('Color').fill('celeste')
  await page.getByRole('button', { name: 'Guardar' }).click()
  await expect(page.getByRole('dialog')).toBeHidden()

  await page.reload()
  await expect(page.getByText('Camisa de lino azul')).toBeVisible()
})

test('reintentar vuelve a llenar el formulario sin guardar', async ({
  page,
}) => {
  await page.goto('/guardarropa')
  await page.getByRole('button', { name: 'Subir prenda' }).click()
  await page.setInputFiles('input[type=file]', 'tests/fixtures/camisa.jpg')
  await expect(page.getByLabel('Nombre')).toBeVisible()
  await page.getByLabel('Nombre').fill('Nombre guardado')
  await page.getByRole('button', { name: 'Guardar' }).click()
  await expect(page.getByRole('dialog')).toBeHidden()

  await page.getByRole('button', { name: 'Subir prenda' }).click()
  await page.setInputFiles('input[type=file]', 'tests/fixtures/camisa.jpg')
  await expect(page.getByLabel('Nombre')).toBeVisible()
  await page.getByLabel('Nombre').fill('Nombre sin guardar')
  await page.getByRole('button', { name: 'Reintentar' }).click()

  // The fake analyzer always proposes "Camisa de lino"; it refills the form but
  // must not persist anything until the user presses Guardar.
  await expect(page.getByLabel('Nombre')).toHaveValue('Camisa de lino')

  await page.getByRole('button', { name: 'Cerrar' }).click()
  await expect(page.getByRole('dialog')).toBeHidden()
  await page.reload()
  await expect(page.getByText('Nombre guardado')).toBeVisible()
  await expect(page.getByText('Nombre sin guardar')).toBeHidden()
})

test('muestra el spinner mientras analiza la prenda', async ({ page }) => {
  await page.goto('/guardarropa')
  await page.route('**/api/garments', async (route) => {
    if (route.request().method() === 'POST') {
      await new Promise((resolve) => setTimeout(resolve, 750))
    }
    await route.continue()
  })

  await page.getByRole('button', { name: 'Subir prenda' }).click()
  await page.setInputFiles('input[type=file]', 'tests/fixtures/camisa.jpg')

  await expect(page.getByText('Analizando prenda…')).toBeVisible()
  await expect(page.getByLabel('Nombre')).toBeVisible()
})

test('rechaza una imagen de más de 10 MB antes de subirla', async ({
  page,
}) => {
  await page.goto('/guardarropa')
  await page.getByRole('button', { name: 'Subir prenda' }).click()
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
