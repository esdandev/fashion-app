import { useId } from 'react'
import type { GarmentCategory, Season } from '../../lib/types'
import { CATEGORY_OPTIONS, SEASON_OPTIONS, STYLE_TAG_OPTIONS } from './garmentLabels'
import type { GarmentFormValues } from './garmentUpload'

interface GarmentFormProps {
  values: GarmentFormValues
  onChange: (values: GarmentFormValues) => void
  disabled?: boolean
  onRemove?: () => void
}

const FORMALITY_OPTIONS: ReadonlyArray<{ value: number; label: string }> = [
  { value: 1, label: 'Deportivo' },
  { value: 2, label: 'Casual' },
  { value: 3, label: 'Smart casual' },
  { value: 4, label: 'Formal' },
  { value: 5, label: 'Gala' },
]

const inputClassName =
  'editorial-field'

export function GarmentForm({
  values,
  onChange,
  disabled = false,
  onRemove,
}: GarmentFormProps) {
  const formId = useId()

  function toggleSeason(season: Season) {
    const seasons = values.seasons.includes(season)
      ? values.seasons.filter((value) => value !== season)
      : [...values.seasons, season]

    onChange({ ...values, seasons })
  }

  function toggleStyleTag(tag: string) {
    const selected = values.styleTags.includes(tag)
    if (!selected && values.styleTags.length >= 4) {
      return
    }

    const styleTags = selected
      ? values.styleTags.filter((value) => value !== tag)
      : [...values.styleTags, tag]

    onChange({ ...values, styleTags })
  }

  return (
    <div className="space-y-4">
      <div className="flex items-start justify-between gap-3">
        <p className="font-serif text-xs text-stone-600">
          Completá los datos de esta prenda. Todos los campos son manuales.
        </p>
        {onRemove && (
          <button
            type="button"
            onClick={onRemove}
            disabled={disabled}
            className="editorial-link shrink-0 text-stone-500 hover:text-oxblood disabled:cursor-not-allowed disabled:opacity-50"
          >
            Quitar
          </button>
        )}
      </div>

      <div className="grid gap-4 sm:grid-cols-2">
        <label
          htmlFor={`${formId}-name`}
          className="editorial-label flex flex-col gap-1 sm:col-span-2"
        >
          Nombre
          <input
            id={`${formId}-name`}
            type="text"
            required
            maxLength={120}
            value={values.name}
            onChange={(event) =>
              onChange({ ...values, name: event.target.value })
            }
            disabled={disabled}
            className={inputClassName}
            placeholder="Camisa de lino"
          />
        </label>

        <label
          htmlFor={`${formId}-category`}
          className="editorial-label flex flex-col gap-1"
        >
          Categoría
          <select
            id={`${formId}-category`}
            value={values.category}
            onChange={(event) =>
              onChange({
                ...values,
                category: event.target.value as GarmentCategory,
              })
            }
            disabled={disabled}
            className={inputClassName}
          >
            {CATEGORY_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </label>

        <label
          htmlFor={`${formId}-color`}
          className="editorial-label flex flex-col gap-1"
        >
          Color
          <input
            id={`${formId}-color`}
            type="text"
            maxLength={40}
            value={values.colorName}
            onChange={(event) =>
              onChange({ ...values, colorName: event.target.value })
            }
            disabled={disabled}
            className={inputClassName}
            placeholder="beige"
          />
        </label>

        <label
          htmlFor={`${formId}-color-hex`}
          className="editorial-label flex flex-col gap-1"
        >
          Color (hex)
          <span className="flex items-center gap-2">
            <span
              aria-hidden="true"
              className="size-6 shrink-0 rounded-full border border-stone-300"
              style={{ backgroundColor: values.colorHex }}
            />
            <input
              id={`${formId}-color-hex`}
              type="text"
              maxLength={7}
              value={values.colorHex}
              onChange={(event) =>
                onChange({ ...values, colorHex: event.target.value })
              }
              disabled={disabled}
              className={inputClassName}
              placeholder="#f5f5dc"
            />
          </span>
          <span className="font-serif text-[11px] font-normal text-stone-500">
            Opcional. Se usa para la muestra de color de la tarjeta.
          </span>
        </label>

        <label
          htmlFor={`${formId}-formality`}
          className="editorial-label flex flex-col gap-1"
        >
          Formalidad
          <select
            id={`${formId}-formality`}
            value={values.formality}
            onChange={(event) =>
              onChange({ ...values, formality: Number(event.target.value) })
            }
            disabled={disabled}
            className={inputClassName}
          >
            {FORMALITY_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.value} · {option.label}
              </option>
            ))}
          </select>
        </label>
      </div>

      <fieldset disabled={disabled} className="border-0 p-0">
        <legend className="editorial-label mb-2">Temporada</legend>
        <div className="flex flex-wrap gap-x-5 gap-y-2">
          {SEASON_OPTIONS.map((option) => (
            <label
              key={option.value}
              className="inline-flex items-center gap-2 font-sans text-sm text-stone-700"
            >
              <input
                type="checkbox"
                checked={values.seasons.includes(option.value)}
                onChange={() => toggleSeason(option.value)}
                disabled={disabled}
                className="size-4 rounded-none border-stone-400 accent-oxblood"
              />
              {option.label}
            </label>
          ))}
        </div>
      </fieldset>

      <fieldset disabled={disabled} className="border-0 p-0">
        <legend className="editorial-label mb-1">Etiquetas</legend>
        <p className="mb-2 font-serif text-[11px] font-normal text-stone-500">
          Elegí hasta 4 del vocabulario controlado.
        </p>
        <div className="flex flex-wrap gap-2">
          {STYLE_TAG_OPTIONS.map((option) => {
            const selected = values.styleTags.includes(option.value)
            const disabledByLimit = !selected && values.styleTags.length >= 4

            return (
              <label
                key={option.value}
                className={`inline-flex cursor-pointer items-center gap-1.5 border px-2.5 py-1 font-sans text-[10px] tracking-[0.04em] transition-colors ${
                  selected
                    ? 'border-ink bg-ink text-paper'
                    : 'border-stone-400 bg-white text-stone-700 hover:border-ink'
                } ${disabledByLimit ? 'cursor-not-allowed opacity-40' : ''}`}
              >
                <input
                  type="checkbox"
                  value={option.value}
                  checked={selected}
                  onChange={() => toggleStyleTag(option.value)}
                  disabled={disabled || disabledByLimit}
                  className="sr-only"
                />
                {option.label}
              </label>
            )
          })}
        </div>
      </fieldset>
    </div>
  )
}
