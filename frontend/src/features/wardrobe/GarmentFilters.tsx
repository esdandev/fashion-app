import type { GarmentFiltersState } from '../../lib/types'
import { CATEGORY_OPTIONS, SEASON_OPTIONS } from './garmentLabels'

interface GarmentFiltersProps {
  filters: GarmentFiltersState
  onChange: (filters: GarmentFiltersState) => void
}

const selectClassName =
  'w-full rounded-xl border border-stone-300 bg-white px-3 py-2 text-sm text-stone-800 shadow-sm transition-colors focus:border-stone-500 focus:ring-2 focus:ring-stone-300 focus:outline-none sm:w-auto'

export function GarmentFilters({ filters, onChange }: GarmentFiltersProps) {
  const hasFilters = Boolean(filters.category || filters.season)

  return (
    <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
      <label className="flex flex-col gap-1 text-xs font-medium text-stone-500">
        Categoría
        <select
          value={filters.category}
          onChange={(event) =>
            onChange({
              ...filters,
              category: event.target.value as GarmentFiltersState['category'],
            })
          }
          className={selectClassName}
        >
          <option value="">Todas</option>
          {CATEGORY_OPTIONS.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
      </label>

      <label className="flex flex-col gap-1 text-xs font-medium text-stone-500">
        Temporada
        <select
          value={filters.season}
          onChange={(event) =>
            onChange({
              ...filters,
              season: event.target.value as GarmentFiltersState['season'],
            })
          }
          className={selectClassName}
        >
          <option value="">Todas</option>
          {SEASON_OPTIONS.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
      </label>

      {hasFilters && (
        <button
          type="button"
          onClick={() => onChange({ category: '', season: '' })}
          className="self-start rounded-xl px-3 py-2 text-sm font-medium text-stone-600 underline-offset-4 transition-colors hover:text-stone-900 hover:underline sm:self-auto"
        >
          Limpiar filtros
        </button>
      )}
    </div>
  )
}
