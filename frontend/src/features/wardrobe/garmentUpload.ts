import type { Garment, GarmentCategory, Season } from '../../lib/types'
import { STYLE_TAG_VALUES } from './garmentLabels'

/** Same limit enforced by `POST /api/garments`. */
export const MAX_GARMENT_FILE_BYTES = 10 * 1024 * 1024

const ACCEPTED_MIME_TYPES = new Set([
  'image/jpeg',
  'image/png',
  'image/webp',
])

const ACCEPTED_EXTENSIONS = ['.jpg', '.jpeg', '.png', '.webp']

/**
 * Editable shape for the manual form. Tags are a bounded multi-select over the
 * closed style vocabulary, never free text.
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

/** Validates the manual attributes before sending them to the API. */
export function validateGarmentValues(values: GarmentFormValues): string | null {
  if (values.name.trim().length === 0) {
    return 'El nombre no puede estar vacío.'
  }

  if (values.name.trim().length > 120) {
    return 'El nombre no puede superar los 120 caracteres.'
  }

  if (
    values.colorHex.trim().length > 0 &&
    !/^#[0-9a-fA-F]{6}$/.test(values.colorHex.trim())
  ) {
    return 'El color hexadecimal debe tener el formato #RRGGBB.'
  }

  if (values.formality < 1 || values.formality > 5) {
    return 'La formalidad debe estar entre 1 y 5.'
  }

  if (!values.styleTags.every((tag) => STYLE_TAG_VALUES.includes(tag))) {
    return 'Hay una etiqueta de estilo inválida.'
  }

  return null
}

/** Prefills the form with a readable name derived from the file name. */
export function garmentValuesFromFile(file: File): GarmentFormValues {
  const name = file.name
    .replace(/\.[^.]+$/, '')
    .replace(/[-_]+/g, ' ')
    .trim()

  return {
    ...EMPTY_GARMENT_FORM_VALUES,
    name: name.length > 0 ? name.slice(0, 120) : 'Prenda',
  }
}

export function garmentFormValuesFromGarment(
  garment: Garment,
): GarmentFormValues {
  return {
    name: garment.name,
    category: garment.category,
    colorName: garment.colorName,
    colorHex: garment.colorHex,
    seasons: garment.seasons,
    formality: garment.formality,
    styleTags: garment.styleTags,
  }
}
