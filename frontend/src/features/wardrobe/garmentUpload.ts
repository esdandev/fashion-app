import type {
  ConfirmGarmentRequest,
  CurrentGarmentValues,
  Garment,
  GarmentAnalysisProposal,
  GarmentCategory,
  PatchGarmentRequest,
  Season,
} from '../../lib/types'
import { STYLE_TAG_VALUES } from './garmentLabels'

/** Same limit enforced by `POST /api/garments`. */
export const MAX_GARMENT_FILE_BYTES = 10 * 1024 * 1024

const ACCEPTED_MIME_TYPES = new Set([
  'image/jpeg',
  'image/png',
  'image/webp',
])

const ACCEPTED_EXTENSIONS = ['.jpg', '.jpeg', '.png', '.webp']

const CATEGORIES: readonly GarmentCategory[] = [
  'top',
  'bottom',
  'dress',
  'outerwear',
  'shoes',
  'bag',
  'accessory',
]

const SEASONS: readonly Season[] = ['spring', 'summer', 'autumn', 'winter']

/**
 * Editable shape shown in the confirmation form. Tags are a bounded
 * multi-select over the closed style vocabulary, never free text.
 */
export interface GarmentFormValues {
  name: string
  category: GarmentCategory
  colorName: string
  colorHex: string
  seasons: Season[]
  formality: number
  styleTags: string[]
}

export const EMPTY_GARMENT_FORM_VALUES: GarmentFormValues = {
  name: '',
  category: 'top',
  colorName: '',
  colorHex: '',
  seasons: [],
  formality: 3,
  styleTags: [],
}

/**
 * Client-side gate before uploading. Returns a Spanish error message, or
 * `null` when the file is acceptable.
 */
export function validateGarmentFile(
  file: Pick<File, 'name' | 'size' | 'type'>,
): string | null {
  const name = file.name.toLowerCase()
  const typeIsKnown = file.type.length > 0
  const typeIsAccepted = typeIsKnown
    ? ACCEPTED_MIME_TYPES.has(file.type)
    : ACCEPTED_EXTENSIONS.some((extension) => name.endsWith(extension))

  if (!typeIsAccepted) {
    return 'Solo se aceptan imágenes JPEG, PNG o WebP.'
  }

  if (file.size > MAX_GARMENT_FILE_BYTES) {
    return 'La imagen no puede superar los 10 MB.'
  }

  return null
}

export function garmentFormValuesFromGarment(
  garment: Garment,
): GarmentFormValues {
  return {
    name: garment.name,
    category: garment.category,
    colorName: garment.colorName,
    colorHex: garment.colorHex,
    seasons: garment.seasons
      .map(parseSeason)
      .filter((season): season is Season => season !== null),
    formality: clampFormality(garment.formality),
    styleTags: normalizeStyleTags(garment.styleTags),
  }
}

/**
 * Form values for a photo that is already saved. The analyze response carries
 * these columns so re-uploading never reverts edits made in a previous save.
 */
export function garmentFormValuesFromCurrentGarment(
  current: CurrentGarmentValues,
): GarmentFormValues {
  return {
    name: current.name,
    category: parseCategory(current.category) ?? 'top',
    colorName: current.colorName,
    colorHex: current.colorHex,
    seasons: current.seasons
      .map(parseSeason)
      .filter((season): season is Season => season !== null),
    formality: clampFormality(current.formality),
    styleTags: normalizeStyleTags(current.styleTags),
  }
}

/**
 * Applies a reanalysis proposal over the current form. Values the model did not
 * return fall back to what the user already had, so a partial proposal never
 * blanks the confirmation form.
 */
export function garmentFormValuesFromProposal(
  proposal: GarmentAnalysisProposal,
  current: GarmentFormValues,
): GarmentFormValues {
  const seasons = proposal.seasons
    .map(parseSeason)
    .filter((season): season is Season => season !== null)
  const color = proposal.colors.find((value) => value.trim().length > 0)
  const category = parseCategory(proposal.category)

  return {
    name: proposal.name.trim() || current.name,
    category: category ?? current.category,
    colorName: color?.trim() ?? current.colorName,
    colorHex: current.colorHex,
    seasons: seasons.length > 0 ? seasons : current.seasons,
    formality: clampFormality(proposal.formality),
    styleTags: normalizeStyleTags(proposal.styleTags),
  }
}

export function toPatchRequest(values: GarmentFormValues): PatchGarmentRequest {
  const colorHex = values.colorHex.trim()

  return {
    name: values.name.trim(),
    category: values.category,
    colorName: values.colorName.trim(),
    ...(colorHex ? { colorHex } : {}),
    seasons: values.seasons,
    formality: clampFormality(values.formality),
    styleTags: normalizeStyleTags(values.styleTags),
  }
}

/**
 * Builds the confirm payload from the editable values. Optional analysis fields
 * that are not part of the form (subcategory, pattern, material) are passed
 * through so confirming does not discard what the AI detected.
 */
export function toConfirmRequest(
  values: GarmentFormValues,
  contentHash: string,
  options: {
    confidence?: number
    subcategory?: string | null
    pattern?: string | null
    material?: string | null
    notes?: string | null
  } = {},
): ConfirmGarmentRequest {
  const colorHex = values.colorHex.trim()

  return {
    contentHash,
    name: values.name.trim(),
    category: values.category,
    subcategory: options.subcategory ?? null,
    colorName: values.colorName.trim(),
    ...(colorHex ? { colorHex } : {}),
    pattern: options.pattern ?? null,
    seasons: values.seasons,
    formality: clampFormality(values.formality),
    material: options.material ?? null,
    styleTags: normalizeStyleTags(values.styleTags),
    notes: options.notes ?? null,
    confidence: options.confidence,
  }
}

/** Keeps only the closed vocabulary, deduplicated and capped at four tags. */
export function normalizeStyleTags(tags: readonly string[]): string[] {
  const seen = new Set<string>()
  const result: string[] = []

  for (const tag of tags) {
    const normalized = tag.trim().toLowerCase()
    if (
      normalized.length === 0 ||
      !STYLE_TAG_VALUES.includes(normalized) ||
      seen.has(normalized)
    ) {
      continue
    }

    seen.add(normalized)
    result.push(normalized)
    if (result.length === 4) {
      break
    }
  }

  return result
}

function parseCategory(value: string): GarmentCategory | null {
  const normalized = value.trim().toLowerCase() as GarmentCategory
  return CATEGORIES.includes(normalized) ? normalized : null
}

function parseSeason(value: string): Season | null {
  const normalized = value.trim().toLowerCase() as Season
  return SEASONS.includes(normalized) ? normalized : null
}

function clampFormality(value: number): number {
  if (!Number.isFinite(value)) {
    return 3
  }

  return Math.min(5, Math.max(1, Math.round(value)))
}
