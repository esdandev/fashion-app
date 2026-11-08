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
