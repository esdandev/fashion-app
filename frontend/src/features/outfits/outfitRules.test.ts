import { describe, expect, it } from 'vitest'
import {
  OUTFIT_MESSAGES,
  canSaveOutfit,
  hasPossibleOutfit,
  validateOutfitGarments,
} from './outfitRules'
import type { Garment, GarmentCategory } from '../../lib/types'

function garment(category: GarmentCategory, formality: number) {
  return { category, formality }
}

function fullGarment(
  id: string,
  category: GarmentCategory,
  formality: number,
): Garment {
  return {
    id,
    name: id,
    category,
    colorName: 'negro',
    colorHex: '#111111',
    formality,
    confidence: 0.9,
    seasons: ['spring'],
    styleTags: ['casual'],
    imageUrl: `/media/${id}.webp`,
    thumbUrl: `/media/${id}-thumb.webp`,
  }
}

describe('canSaveOutfit', () => {
  it('requires shoes and a top+bottom or a dress', () => {
    expect(canSaveOutfit(['top', 'bottom'])).toBe(false)
    expect(canSaveOutfit(['top', 'bottom', 'shoes'])).toBe(true)
    expect(canSaveOutfit(['dress', 'shoes'])).toBe(true)
    expect(canSaveOutfit(['shoes'])).toBe(false)
  })

  it('accepts garments and rejects a formality spread greater than two', () => {
    // Boundary: max - min === 2 is still valid…
    expect(
      canSaveOutfit([
        garment('top', 2),
        garment('bottom', 4),
        garment('shoes', 3),
      ]),
    ).toBe(true)

    // …but 3 points apart is not.
    expect(
      canSaveOutfit([
        garment('top', 1),
        garment('bottom', 4),
        garment('shoes', 3),
      ]),
    ).toBe(false)
  })

  it('explains the formality rule in Spanish', () => {
    const validation = validateOutfitGarments([
      garment('top', 1),
      garment('bottom', 4),
      garment('shoes', 3),
    ])

    expect(validation.canSave).toBe(false)
    expect(validation.message).toBe(OUTFIT_MESSAGES.formalitySpread)
    expect(validation.message).toBe(
      'La formalidad de las prendas no puede variar más de 2 puntos.',
    )
  })
})

describe('hasPossibleOutfit', () => {
  it('finds a combinational outfit only when categories and formality fit', () => {
    expect(hasPossibleOutfit([])).toBe(false)
    expect(
      hasPossibleOutfit([
        fullGarment('camisa', 'top', 3),
        fullGarment('jean', 'bottom', 3),
      ]),
    ).toBe(false)
    expect(
      hasPossibleOutfit([
        fullGarment('camisa', 'top', 1),
        fullGarment('jean', 'bottom', 4),
        fullGarment('zapatillas', 'shoes', 3),
      ]),
    ).toBe(false)
    expect(
      hasPossibleOutfit([
        fullGarment('camisa', 'top', 3),
        fullGarment('jean', 'bottom', 4),
        fullGarment('zapatillas', 'shoes', 2),
      ]),
    ).toBe(true)
  })

  it('accepts a dress with shoes and ignores unrelated accessories', () => {
    expect(
      hasPossibleOutfit([
        fullGarment('vestido', 'dress', 4),
        fullGarment('zapatillas', 'shoes', 4),
        fullGarment('bolso', 'bag', 1),
      ]),
    ).toBe(true)
  })
})
