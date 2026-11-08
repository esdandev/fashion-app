import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, api } from './api'
import type { Garment } from './types'

function garment(id: string): Garment {
  return {
    id,
    name: `Prenda ${id}`,
    category: 'top',
    colorName: 'negro',
    colorHex: '#111111',
    formality: 3,
    confidence: 0.9,
    seasons: ['spring'],
    styleTags: [],
    imageUrl: `/media/${id}.webp`,
    thumbUrl: `/media/${id}-thumb.webp`,
  }
}

afterEach(() => {
  vi.restoreAllMocks()
})

describe('api.getAllGarments', () => {
  it('walks the cursor pages until the wardrobe ends', async () => {
    const spy = vi
      .spyOn(api, 'getGarments')
      .mockResolvedValueOnce({ items: [garment('1')], nextCursor: 'cursor-1' })
      .mockResolvedValueOnce({ items: [garment('2')], nextCursor: null })

    const garments = await api.getAllGarments()

    expect(garments.map((item) => item.id)).toEqual(['1', '2'])
    expect(spy).toHaveBeenCalledTimes(2)
    expect(spy).toHaveBeenLastCalledWith('?cursor=cursor-1', undefined)
  })

  it('stops after five pages so a huge closet stays bounded', async () => {
    const spy = vi.spyOn(api, 'getGarments').mockResolvedValue({
      items: [garment('x')],
      nextCursor: 'more',
    })

    const garments = await api.getAllGarments()

    expect(spy).toHaveBeenCalledTimes(5)
    expect(garments).toHaveLength(5)
  })
})

describe('ApiError Retry-After', () => {
  it('exposes the quota reset in seconds from a 429 response', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(
        JSON.stringify({
          detail: 'Se alcanzó el límite de sugerencias de IA para hoy (UTC).',
        }),
        {
          status: 429,
          headers: {
            'Content-Type': 'application/problem+json',
            'Retry-After': '120',
          },
        },
      ),
    )

    await expect(
      api.suggestOutfits({ weather: 'templado' }),
    ).rejects.toMatchObject({
      status: 429,
      retryAfterSeconds: 120,
    })
  })

  it('leaves Retry-After undefined when the header is missing', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(
        JSON.stringify({
          detail: 'Se alcanzó el límite de sugerencias de IA para hoy (UTC).',
        }),
        {
          status: 429,
          headers: { 'Content-Type': 'application/problem+json' },
        },
      ),
    )

    const caught = await api
      .suggestOutfits({ weather: 'templado' })
      .catch((error: unknown) => error)

    expect(caught).toBeInstanceOf(ApiError)
    expect((caught as ApiError).status).toBe(429)
    expect((caught as ApiError).retryAfterSeconds).toBeUndefined()
  })
})
