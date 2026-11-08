import { describe, expect, it } from 'vitest'
import {
  OUTFIT_MESSAGES,
  canSaveOutfit,
  validateOutfitGarments,
} from './outfitRules'
import type { GarmentCategory } from '../../lib/types'

function garment(category: GarmentCategory, formality: number) {
  return { category, formality }
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
