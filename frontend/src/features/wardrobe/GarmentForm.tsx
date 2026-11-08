import type { FormEvent } from 'react'
import type { GarmentCategory, Season } from '../../lib/types'
import { CATEGORY_OPTIONS, SEASON_OPTIONS } from './garmentLabels'
import type { GarmentFormValues } from './garmentUpload'

interface GarmentFormProps {
  values: GarmentFormValues
  onChange: (values: GarmentFormValues) => void
  onSave: () => void
  onRetry: () => void
  isSaving: boolean
  isReanalyzing: boolean
  confidence?: number
  error?: string | null
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
  onSave,
  onRetry,
  isSaving,
  isReanalyzing,
  confidence,
  error,
}: GarmentFormProps) {
  const busy = isSaving || isReanalyzing

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!busy && values.name.trim().length > 0) {
      onSave()
    }
  }

  function toggleSeason(season: Season) {
    const seasons = values.seasons.includes(season)
      ? values.seasons.filter((value) => value !== season)
      : [...values.seasons, season]

    onChange({ ...values, seasons })
  }

  return (
    <form
      aria-busy={busy}
      onSubmit={handleSubmit}
      className="mt-5 space-y-5 border-t border-stone-200 pt-5"
    >
      <div>
        <h3 className="text-sm font-semibold text-stone-900">
          Confirmá los datos
        </h3>
        <p className="mt-0.5 text-xs text-stone-500">
          La IA propuso estos atributos. Corregí lo que haga falta antes de
          guardar.
        </p>
        {typeof confidence === 'number' && (
          <p
            className={`mt-2 inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium ${
              confidence < 0.6
                ? 'bg-amber-50 text-amber-900'
                : 'bg-stone-100 text-stone-600'
            }`}
          >
            Confianza del análisis: {Math.round(confidence * 100)}%
            {confidence < 0.6 && ' · revisá los datos'}
          </p>
        )}
      </div>

      {error && (
        <div
          role="alert"
          className="rounded-xl border border-red-200 bg-red-50 p-3 text-sm text-red-900"
        >
          {error}
        </div>
      )}

      <div className="grid gap-4 sm:grid-cols-2">
        <label
          htmlFor="garment-name"
          className="flex flex-col gap-1 text-xs font-medium text-stone-500 sm:col-span-2"
        >
          Nombre
          <input
            id="garment-name"
            name="name"
            type="text"
            required
            maxLength={120}
            value={values.name}
            onChange={(event) =>
              onChange({ ...values, name: event.target.value })
            }
            disabled={busy}
            className={inputClassName}
            placeholder="Camisa de lino"
          />
        </label>

        <label
          htmlFor="garment-category"
          className="flex flex-col gap-1 text-xs font-medium text-stone-500"
        >
          Categoría
          <select
            id="garment-category"
            name="category"
            value={values.category}
            onChange={(event) =>
              onChange({
                ...values,
                category: event.target.value as GarmentCategory,
              })
            }
            disabled={busy}
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
          htmlFor="garment-color"
          className="flex flex-col gap-1 text-xs font-medium text-stone-500"
        >
          Color
          <input
            id="garment-color"
            name="colorName"
            type="text"
            maxLength={40}
            value={values.colorName}
            onChange={(event) =>
              onChange({ ...values, colorName: event.target.value })
            }
            disabled={busy}
            className={inputClassName}
            placeholder="beige"
          />
        </label>

        <label
          htmlFor="garment-formality"
          className="flex flex-col gap-1 text-xs font-medium text-stone-500"
        >
          Formalidad
          <select
            id="garment-formality"
            name="formality"
            value={values.formality}
            onChange={(event) =>
              onChange({ ...values, formality: Number(event.target.value) })
            }
            disabled={busy}
            className={inputClassName}
          >
            {FORMALITY_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.value} · {option.label}
              </option>
            ))}
          </select>
        </label>

        <label
          htmlFor="garment-tags"
          className="flex flex-col gap-1 text-xs font-medium text-stone-500"
        >
          Etiquetas
          <input
            id="garment-tags"
            name="styleTags"
            type="text"
            value={values.styleTags}
            onChange={(event) =>
              onChange({ ...values, styleTags: event.target.value })
            }
            disabled={busy}
            className={inputClassName}
            placeholder="minimalista, casual"
          />
          <span className="text-[11px] font-normal text-stone-400">
            Separadas por coma.
          </span>
        </label>
      </div>

      <fieldset disabled={busy} className="border-0 p-0">
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
                disabled={busy}
                className="size-4 rounded border-stone-300 text-stone-900 focus:ring-stone-400"
              />
              {option.label}
            </label>
          ))}
        </div>
      </fieldset>

      <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
        <button
          type="button"
          onClick={onRetry}
          disabled={busy}
          className="inline-flex items-center justify-center gap-2 rounded-xl border border-stone-300 bg-white px-4 py-2 text-sm font-medium text-stone-800 shadow-sm transition-colors hover:bg-stone-100 disabled:cursor-not-allowed disabled:opacity-60"
        >
          {isReanalyzing && <Spinner />}
          Reintentar
        </button>
        <button
          type="submit"
          disabled={busy || values.name.trim().length === 0}
          className="inline-flex items-center justify-center gap-2 rounded-xl bg-stone-900 px-4 py-2 text-sm font-medium text-white shadow-sm transition-colors hover:bg-stone-700 disabled:cursor-not-allowed disabled:opacity-60"
        >
          {isSaving && <Spinner />}
          Guardar
        </button>
      </div>
    </form>
  )
}

function Spinner() {
  return (
    <svg
      aria-hidden="true"
      viewBox="0 0 24 24"
      fill="none"
      className="size-4 animate-spin"
    >
      <circle
        cx="12"
        cy="12"
        r="9"
        stroke="currentColor"
        strokeWidth="3"
        className="opacity-25"
      />
      <path
        d="M21 12a9 9 0 0 0-9-9"
        stroke="currentColor"
        strokeWidth="3"
        strokeLinecap="round"
      />
    </svg>
  )
}
