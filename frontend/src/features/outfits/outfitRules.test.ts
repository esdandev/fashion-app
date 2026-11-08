import { describe, expect, it } from 'vitest'
import { canSaveOutfit } from './outfitRules'

describe('canSaveOutfit', () => {
  it('requires shoes and a top+bottom or a dress', () => {
    expect(canSaveOutfit(['top', 'bottom'])).toBe(false)
    expect(canSaveOutfit(['top', 'bottom', 'shoes'])).toBe(true)
    expect(canSaveOutfit(['dress', 'shoes'])).toBe(true)
    expect(canSaveOutfit(['shoes'])).toBe(false)
  })
})
