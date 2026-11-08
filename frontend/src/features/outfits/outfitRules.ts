import type { GarmentCategory } from '../../lib/types'

/**
 * Client-side mirror of `OutfitRules.Validate` on the API. It exists only to
 * disable "+ Guardar outfit" and explain why; the server always revalidates.
 */
export interface OutfitValidation {
  canSave: boolean
  /** User-facing Spanish reason, or `null` when the selection is valid. */
  message: string | null
}

export const OUTFIT_MESSAGES = {
  invalidComposition:
    'El outfit necesita una parte superior y una inferior, o un vestido.',
  missingShoes: 'El outfit necesita calzado.',
  multipleOuterwear: 'Solo puede haber un abrigo.',
  duplicateGarment: 'No se puede repetir la misma prenda.',
} as const

export function validateOutfitSelection(
  categories: readonly GarmentCategory[],
): OutfitValidation {
  const count = (category: GarmentCategory) =>
    categories.filter((candidate) => candidate === category).length

  const topCount = count('top')
  const bottomCount = count('bottom')
  const dressCount = count('dress')
  const shoesCount = count('shoes')
  const outerwearCount = count('outerwear')

  // One of each base piece at most: two tops, two bottoms or two dresses are a
  // repeated category even when the garments differ.
  if (topCount > 1 || bottomCount > 1 || dressCount > 1) {
    return invalid(OUTFIT_MESSAGES.duplicateGarment)
  }

  // A dress replaces separates, never adds to them.
  const hasDress = dressCount === 1
  const hasTopAndBottom = topCount === 1 && bottomCount === 1
  if (hasDress && (topCount > 0 || bottomCount > 0)) {
    return invalid(OUTFIT_MESSAGES.invalidComposition)
  }
  if (!hasDress && !hasTopAndBottom) {
    return invalid(OUTFIT_MESSAGES.invalidComposition)
  }

  if (shoesCount === 0) {
    return invalid(OUTFIT_MESSAGES.missingShoes)
  }

  if (outerwearCount > 1) {
    return invalid(OUTFIT_MESSAGES.multipleOuterwear)
  }

  return { canSave: true, message: null }
}

export function canSaveOutfit(
  categories: readonly GarmentCategory[],
): boolean {
  return validateOutfitSelection(categories).canSave
}

function invalid(message: string): OutfitValidation {
  return { canSave: false, message }
}
