import type { Garment, GarmentCategory } from '../../lib/types'

/**
 * Client-side mirror of `OutfitRules.Validate` on the API. It exists only to
 * disable "Guardar outfit" and explain why; the server always revalidates.
 */
export interface OutfitValidation {
  canSave: boolean
  /** User-facing Spanish reason, or `null` when the selection is valid. */
  message: string | null
}

/** The only garment fields the client-side rules need. */
export type OutfitGarment = Pick<Garment, 'category' | 'formality'>

export const OUTFIT_MESSAGES = {
  invalidComposition:
    'El outfit necesita una parte superior y una inferior, o un vestido.',
  missingShoes: 'El outfit necesita calzado.',
  multipleOuterwear: 'Solo puede haber un abrigo.',
  duplicateGarment: 'No se puede repetir la misma prenda.',
  formalitySpread:
    'La formalidad de las prendas no puede variar más de 2 puntos.',
} as const

/** Category-only rules, kept for callers that have no garment data. */
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

/**
 * Full rules: category composition plus the formality spread. This is the
 * version the outfit builder must use.
 */
export function validateOutfitGarments(
  garments: readonly OutfitGarment[],
): OutfitValidation {
  const composition = validateOutfitSelection(
    garments.map((garment) => garment.category),
  )
  if (!composition.canSave) {
    return composition
  }

  const formalities = garments.map((garment) => garment.formality)
  const spread = Math.max(...formalities) - Math.min(...formalities)
  if (spread > 2) {
    return invalid(OUTFIT_MESSAGES.formalitySpread)
  }

  return composition
}

export function canSaveOutfit(categories: readonly GarmentCategory[]): boolean
export function canSaveOutfit(garments: readonly OutfitGarment[]): boolean
export function canSaveOutfit(
  selection: readonly GarmentCategory[] | readonly OutfitGarment[],
): boolean {
  return typeof selection[0] === 'string'
    ? validateOutfitSelection(selection as readonly GarmentCategory[]).canSave
    : validateOutfitGarments(selection as readonly OutfitGarment[]).canSave
}

function invalid(message: string): OutfitValidation {
  return { canSave: false, message }
}
