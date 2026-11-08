import type { GarmentCategory, Season } from '../../lib/types'

export const CATEGORY_OPTIONS: ReadonlyArray<{
  value: GarmentCategory
  label: string
}> = [
  { value: 'top', label: 'Tops' },
  { value: 'bottom', label: 'Bottoms' },
  { value: 'dress', label: 'Vestidos' },
  { value: 'outerwear', label: 'Abrigos' },
  { value: 'shoes', label: 'Calzado' },
  { value: 'bag', label: 'Bolsos' },
  { value: 'accessory', label: 'Accesorios' },
]

export const SEASON_OPTIONS: ReadonlyArray<{
  value: Season
  label: string
}> = [
  { value: 'spring', label: 'Primavera' },
  { value: 'summer', label: 'Verano' },
  { value: 'autumn', label: 'Otoño' },
  { value: 'winter', label: 'Invierno' },
]

/** Same closed list used by the analysis prompt and the digest. */
export const STYLE_TAG_OPTIONS: ReadonlyArray<{
  value: string
  label: string
}> = [
  { value: 'minimalista', label: 'Minimalista' },
  { value: 'clasico', label: 'Clásico' },
  { value: 'casual', label: 'Casual' },
  { value: 'streetwear', label: 'Streetwear' },
  { value: 'romantico', label: 'Romántico' },
  { value: 'bohemio', label: 'Bohemio' },
  { value: 'deportivo', label: 'Deportivo' },
  { value: 'formal', label: 'Formal' },
  { value: 'vintage', label: 'Vintage' },
  { value: 'noventoso', label: 'Noventoso' },
  { value: 'y2k', label: 'Y2K' },
  { value: 'utilitario', label: 'Utilitario' },
  { value: 'monocromatico', label: 'Monocromático' },
  { value: 'colorido', label: 'Colorido' },
  { value: 'oversized', label: 'Oversized' },
  { value: 'entallado', label: 'Entallado' },
  { value: 'capas', label: 'Capas' },
  { value: 'texturado', label: 'Texturado' },
]

export const STYLE_TAG_VALUES: readonly string[] = STYLE_TAG_OPTIONS.map(
  (option) => option.value,
)

const CATEGORY_LABELS = Object.fromEntries(
  CATEGORY_OPTIONS.map((option) => [option.value, option.label]),
) as Record<GarmentCategory, string>

const SEASON_LABELS = Object.fromEntries(
  SEASON_OPTIONS.map((option) => [option.value, option.label]),
) as Record<Season, string>

export function categoryLabel(category: GarmentCategory): string {
  return CATEGORY_LABELS[category] ?? category
}

export function seasonLabel(season: Season): string {
  return SEASON_LABELS[season] ?? season
}
