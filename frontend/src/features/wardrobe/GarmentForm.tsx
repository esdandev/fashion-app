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
  'w-full rounded-xl border border-stone-300 bg-white px-3 py-2 text-sm text-stone-800 shadow-sm transition-colors focus:border-stone-500 focus:ring-2 focus:ring-stone-300 focus:outline-none disabled:bg-stone-100 disabled:text-stone-500'

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
        <p className="text-xs text-stone-500">
          Completá los datos de esta prenda. Todos los campos son manuales.
        </p>
        {onRemove && (
          <button
            type="button"
            onClick={onRemove}
            disabled={disabled}
            className="shrink-0 text-xs font-medium text-stone-500 transition-colors hover:text-red-700 disabled:cursor-not-allowed disabled:opacity-50"
          >
            Quitar
          </button>
        )}
      </div>

      <div className="grid gap-4 sm:grid-cols-2">
        <label
          htmlFor={`${formId}-name`}
          className="flex flex-col gap-1 text-xs font-medium text-stone-500 sm:col-span-2"
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
          className="flex flex-col gap-1 text-xs font-medium text-stone-500"
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
          className="flex flex-col gap-1 text-xs font-medium text-stone-500"
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
          className="flex flex-col gap-1 text-xs font-medium text-stone-500"
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
          <span className="text-[11px] font-normal text-stone-400">
            Opcional. Se usa para la muestra de color de la tarjeta.
          </span>
        </label>

        <label
          htmlFor={`${formId}-formality`}
          className="flex flex-col gap-1 text-xs font-medium text-stone-500"
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
        <legend className="mb-2 text-xs font-medium text-stone-500">
          Temporada
        </legend>
        <div className="flex flex-wrap gap-x-5 gap-y-2">
          {SEASON_OPTIONS.map((option) => (
            <label
              key={option.value}
              className="inline-flex items-center gap-2 text-sm text-stone-700"
            >
              <input
                type="checkbox"
                checked={values.seasons.includes(option.value)}
                onChange={() => toggleSeason(option.value)}
                disabled={disabled}
                className="size-4 rounded border-stone-300 text-stone-900 focus:ring-stone-400"
              />
              {option.label}
            </label>
          ))}
        </div>
      </fieldset>

      <fieldset disabled={disabled} className="border-0 p-0">
        <legend className="mb-1 text-xs font-medium text-stone-500">
          Etiquetas
        </legend>
        <p className="mb-2 text-[11px] font-normal text-stone-400">
          Elegí hasta 4 del vocabulario controlado.
        </p>
        <div className="flex flex-wrap gap-2">
          {STYLE_TAG_OPTIONS.map((option) => {
            const selected = values.styleTags.includes(option.value)
            const disabledByLimit = !selected && values.styleTags.length >= 4

            return (
              <label
                key={option.value}
                className={`inline-flex cursor-pointer items-center gap-1.5 rounded-full border px-2.5 py-1 text-xs transition-colors ${
                  selected
                    ? 'border-stone-900 bg-stone-900 text-white'
                    : 'border-stone-300 bg-white text-stone-700 hover:border-stone-400'
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
