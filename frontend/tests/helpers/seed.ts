import { readFile } from 'node:fs/promises'
import { dirname, extname, join } from 'node:path'
import { fileURLToPath } from 'node:url'
import type { APIRequestContext } from '@playwright/test'

export const API_URL = 'http://localhost:5210'

const fixturesDir = join(dirname(fileURLToPath(import.meta.url)), '..', 'fixtures')

export interface WardrobeSeed {
  fixture: string
  name: string
  category: 'top' | 'bottom' | 'dress' | 'outerwear' | 'shoes' | 'bag' | 'accessory'
  colorName?: string
  colorHex?: string
  seasons?: string
  styleTags?: string
  formality?: number
}

function mimeTypeFor(fixture: string): string {
  return extname(fixture).toLowerCase() === '.webp' ? 'image/webp' : 'image/jpeg'
}

/**
 * Creates a garment through the manual multipart endpoint. E2E tests seed the
 * wardrobe this way instead of driving the batch dialog.
 */
export async function seedGarment(
  request: APIRequestContext,
  seed: WardrobeSeed,
) {
  const response = await request.post(`${API_URL}/api/garments`, {
    multipart: {
      file: {
        name: seed.fixture,
        mimeType: mimeTypeFor(seed.fixture),
        buffer: await readFile(join(fixturesDir, seed.fixture)),
      },
      name: seed.name,
      category: seed.category,
      colorName: seed.colorName ?? '',
      colorHex: seed.colorHex ?? '',
      seasons: seed.seasons ?? 'spring,summer,autumn,winter',
      styleTags: seed.styleTags ?? 'casual',
      formality: String(seed.formality ?? 3),
    },
  })

  if (!response.ok()) {
    if (response.status() === 409) {
      const existing = await findGarmentByName(request, seed.name)
      if (existing) {
        return { garment: existing }
      }
    }

    throw new Error(
      `No se pudo crear ${seed.name}: ${response.status()} ${await response.text()}`,
    )
  }

  return (await response.json()) as { garment: { id: string } }
}

export async function seedWardrobe(
  request: APIRequestContext,
  seeds: WardrobeSeed[],
) {
  for (const seed of seeds) {
    await seedGarment(request, seed)
  }
}

/** Walks every cursor page and returns all active garments. */
export async function listAllGarments(
  request: APIRequestContext,
): Promise<Array<{ id: string; name: string }>> {
  const garments: Array<{ id: string; name: string }> = []
  let cursor = ''

  do {
    const query = cursor ? `?cursor=${encodeURIComponent(cursor)}` : ''
    const response = await request.get(`${API_URL}/api/garments${query}`)
    if (!response.ok()) {
      throw new Error(`No se pudo listar el guardarropa: ${response.status()}`)
    }

    const body = (await response.json()) as {
      items: Array<{ id: string; name: string }>
      nextCursor: string | null
    }
    garments.push(...body.items)
    cursor = body.nextCursor ?? ''
  } while (cursor)

  return garments
}

export async function findGarmentByName(
  request: APIRequestContext,
  name: string,
) {
  const garments = await listAllGarments(request)
  return garments.find((garment) => garment.name === name)
}

/** Archives every active garment so a test starts from an empty wardrobe. */
export async function clearGarments(request: APIRequestContext) {
  const garments = await listAllGarments(request)
  for (const garment of garments) {
    await request.delete(`${API_URL}/api/garments/${garment.id}`)
  }
}
