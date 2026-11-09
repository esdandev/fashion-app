import { readFileSync } from 'node:fs'
import { dirname, extname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { expect, test } from '@playwright/test'
import type { APIRequestContext } from '@playwright/test'

const API_URL = 'http://localhost:5210'
const fixturesDir = join(dirname(fileURLToPath(import.meta.url)), 'fixtures')

interface WardrobeSeed {
  fixture: string
  name: string
  category: 'top' | 'bottom' | 'shoes'
  colorName: string
  formality: number
}

/**
 * Seeds a valid outfit through the API instead of the upload UI: the suggestion
 * test is about the outfit flow, not about the garment confirmation dialog.
 * Analyze stores the normalized files; confirm creates the garment with the
 * values this test needs (the fake analyzer always proposes a top).
 */
async function seedGarment(request: APIRequestContext, seed: WardrobeSeed) {
  const extension = extname(seed.fixture).toLowerCase()
  const mimeType = extension === '.webp' ? 'image/webp' : 'image/jpeg'

  const analyze = await request.post(`${API_URL}/api/garments/analyze`, {
    multipart: {
      file: {
        name: seed.fixture,
        mimeType,
        buffer: readFileSync(join(fixturesDir, seed.fixture)),
      },
    },
  })

  if (!analyze.ok()) {
    throw new Error(
      `No se pudo analizar ${seed.fixture}: ${analyze.status()} ${await analyze.text()}`,
    )
  }

  const { contentHash } = (await analyze.json()) as { contentHash: string }

  const confirm = await request.post(`${API_URL}/api/garments/confirm`, {
    data: {
      contentHash,
      name: seed.name,
      category: seed.category,
      colorName: seed.colorName,
      seasons: ['spring', 'summer', 'autumn'],
      formality: seed.formality,
      styleTags: ['casual'],
      confidence: 0.9,
    },
  })

  if (!confirm.ok()) {
    throw new Error(
      `No se pudo confirmar ${seed.name}: ${confirm.status()} ${await confirm.text()}`,
    )
  }
}

async function seedWardrobe(request: APIRequestContext) {
  await seedGarment(request, {
    fixture: 'camisa-lino.webp',
    name: 'Top de prueba E2E',
    category: 'top',
    colorName: 'beige',
    formality: 3,
  })
  await seedGarment(request, {
    fixture: 'pantalon-beige.webp',
    name: 'Pantalón sastre beige',
    category: 'bottom',
    colorName: 'beige',
    formality: 3,
  })
  await seedGarment(request, {
    fixture: 'zapatillas-blancas.webp',
    name: 'Zapatillas blancas',
    category: 'shoes',
    colorName: 'blanco',
    formality: 3,
  })
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
  await expect(page.getByText('Propuesta de dos piezas').first()).toBeVisible()

  const proposal = page.getByTestId('suggestion-card').first()
  await expect(proposal.getByText(/Propuesta de dos piezas/)).toBeVisible()
  await proposal.getByRole('button', { name: 'Guardar' }).click()

  await expect(page.getByText('Outfit guardado en tu lista.')).toBeVisible()

  // Reloading drops the unsaved proposals: the card that remains is the outfit
  // persisted with source=ai.
  await page.reload()
  const savedCard = page
    .getByRole('article')
    .filter({ hasText: 'Propuesta de dos piezas' })
    .first()
  await expect(savedCard).toBeVisible()
  await expect(savedCard.getByText('IA', { exact: true })).toBeVisible()
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
