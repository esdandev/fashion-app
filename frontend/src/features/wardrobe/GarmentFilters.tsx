import type { GarmentFiltersState } from '../../lib/types'
import { CATEGORY_OPTIONS, SEASON_OPTIONS } from './garmentLabels'

interface GarmentFiltersProps {
  filters: GarmentFiltersState
  onChange: (filters: GarmentFiltersState) => void
  colorOptions?: readonly string[]
}

export function GarmentFilters({
  filters,
  onChange,
  colorOptions = [],
}: GarmentFiltersProps) {
  const hasFilters = Boolean(filters.category || filters.color || filters.season)

  return (
    <div className="flex flex-col gap-3 sm:flex-row sm:items-end sm:gap-8">
      <label className="flex min-w-36 flex-col gap-1">
        <span className="editorial-label">Categoría</span>
        <select
          value={filters.category}
          onChange={(event) =>
            onChange({
              ...filters,
              category: event.target.value as GarmentFiltersState['category'],
            })
          }
          className="editorial-field"
        >
          <option value="">Todas</option>
          {CATEGORY_OPTIONS.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
      </label>

      <label className="flex min-w-36 flex-col gap-1">
        <span className="editorial-label">Color</span>
        <input
          type="text"
          list="wardrobe-color-options"
          value={filters.color}
          onChange={(event) =>
            onChange({ ...filters, color: event.target.value })
          }
          placeholder="Todos"
          className="editorial-field"
        />
        <datalist id="wardrobe-color-options">
          {colorOptions.map((color) => (
            <option key={color} value={color} />
          ))}
        </datalist>
      </label>

      <label className="flex min-w-36 flex-col gap-1">
        <span className="editorial-label">Temporada</span>
        <select
          value={filters.season}
          onChange={(event) =>
            onChange({
              ...filters,
              season: event.target.value as GarmentFiltersState['season'],
            })
          }
          className="editorial-field"
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
          onClick={() => onChange({ category: '', color: '', season: '' })}
          className="editorial-link self-start pb-2 text-stone-600 sm:self-auto"
        >
          Limpiar filtros
        </button>
      )}
    </div>
  )
}
