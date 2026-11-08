import { afterEach, describe, expect, it, vi } from 'vitest'
import { api } from './api'
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
