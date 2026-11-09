import { expect, test } from '@playwright/test'
import type { APIRequestContext } from '@playwright/test'
import { API_URL, seedWardrobe as seedGarments } from './helpers/seed'

/** Seeds a valid outfit through the manual garment endpoint. */
async function seedWardrobe(request: APIRequestContext) {
  await seedGarments(request, [
    {
      fixture: 'camisa-lino.webp',
      name: 'Top de prueba E2E',
      category: 'top',
      colorName: 'beige',
      formality: 3,
    },
    {
      fixture: 'pantalon-beige.webp',
      name: 'Pantalón sastre beige',
      category: 'bottom',
      colorName: 'beige',
      formality: 3,
    },
    {
      fixture: 'zapatillas-blancas.webp',
      name: 'Zapatillas blancas',
      category: 'shoes',
      colorName: 'blanco',
      formality: 3,
    },
  ])
}

/**
 * The isolated database is reset per run, not per test. CI retries and partial
 * reruns reuse it, so every outfit test starts from an empty list; otherwise a
 * retry would find the cards created by its failed first attempt.
 */
test.beforeEach(async ({ request }) => {
  const response = await request.get(`${API_URL}/api/outfits`)
  expect(response.ok()).toBeTruthy()

  const outfits = (await response.json()) as Array<{ id: string }>
  for (const outfit of outfits) {
    const deleted = await request.delete(
      `${API_URL}/api/outfits/${outfit.id}`,
    )
    expect(deleted.ok()).toBeTruthy()
  }
})

test('sugiere un outfit y lo guarda en el listado', async ({
  page,
  request,
}) => {
  await seedWardrobe(request)
  await page.goto('/outfits')

  await page.getByRole('button', { name: 'Sugerir outfits' }).click()
  await page.getByLabel('Ocasión').selectOption('trabajo')
  await page.getByLabel('Clima').selectOption('templado')
  await page.getByRole('button', { name: 'Generar' }).click()

  await expect(
    page.getByRole('heading', { name: 'Outfit sugerido' }),
  ).toBeVisible()
  await expect(page.getByText('Look de dos piezas').first()).toBeVisible()

  const proposal = page.getByTestId('suggestion-card').first()
  await expect(proposal.getByText(/Look de dos piezas/)).toBeVisible()
  await proposal.getByRole('button', { name: 'Guardar' }).click()

  await expect(page.getByText('Outfit guardado en tu lista.')).toBeVisible()

  // Reloading drops the unsaved proposals: the card that remains is the outfit
  // persisted with source=ai.
  await page.reload()
  const savedCard = page
    .getByRole('article')
    .filter({ hasText: 'Look de dos piezas' })
    .first()
  await expect(savedCard).toBeVisible()
})

test('crea un outfit manualmente y edita el nombre', async ({
  page,
  request,
}) => {
  // A retry starts from a clean outfit list, but unique names keep the card
  // locators unambiguous even if a previous attempt failed after saving.
  const suffix = `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`
  const originalName = `Look manual ${suffix}`
  const renamedName = `Look renombrado ${suffix}`

  await seedWardrobe(request)
  await page.goto('/outfits')

  await page.getByRole('button', { name: 'Crear outfit' }).click()

  const builder = page.getByTestId('outfit-builder')
  await builder.getByRole('checkbox', { name: /Top de prueba E2E/ }).click()
  await builder
    .getByRole('checkbox', { name: /Pantalón sastre beige/ })
    .click()
  await builder.getByRole('checkbox', { name: /Zapatillas blancas/ }).click()
  await builder.getByLabel('Nombre').fill(originalName)
  await builder.getByRole('button', { name: 'Guardar outfit' }).click()

  const createdCard = page
    .getByRole('article')
    .filter({ hasText: originalName })
    .first()
  await expect(createdCard).toBeVisible()

  await createdCard.getByRole('button', { name: 'Editar' }).click()
  const editor = page.getByTestId('outfit-builder')
  await editor.getByLabel('Nombre').fill(renamedName)
  await editor.getByRole('button', { name: 'Guardar outfit' }).click()

  await expect(
    page.getByRole('article').filter({ hasText: renamedName }).first(),
  ).toBeVisible()
  await expect(
    page.getByRole('article').filter({ hasText: originalName }),
  ).toHaveCount(0)
})

test('elimina un outfit guardado', async ({ page, request }) => {
  const suffix = `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`
  const name = `Look a eliminar ${suffix}`

  await seedWardrobe(request)
  await page.goto('/outfits')

  await page.getByRole('button', { name: 'Crear outfit' }).click()
  const builder = page.getByTestId('outfit-builder')
  await builder.getByRole('checkbox', { name: /Top de prueba E2E/ }).click()
  await builder
    .getByRole('checkbox', { name: /Pantalón sastre beige/ })
    .click()
  await builder.getByRole('checkbox', { name: /Zapatillas blancas/ }).click()
  await builder.getByLabel('Nombre').fill(name)
  await builder.getByRole('button', { name: 'Guardar outfit' }).click()

  const card = page
    .getByRole('article')
    .filter({ hasText: name })
    .first()
  await expect(card).toBeVisible()

  await card.getByRole('button', { name: 'Eliminar' }).click()
  await card.getByRole('button', { name: 'Sí, eliminar' }).click()

  await expect(page.getByRole('article').filter({ hasText: name })).toHaveCount(0)
})
