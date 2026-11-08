import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useId, useState } from 'react'
import type { FormEvent } from 'react'
import { ApiError, api } from '../../lib/api'
import type {
  Garment,
  OutfitDraft,
  OutfitItem,
  OutfitWeather,
} from '../../lib/types'
import { OutfitCollage } from './OutfitCollage'
import { hasPossibleOutfit } from './outfitRules'

const OCCASION_OPTIONS = [
  { value: '', label: 'Cualquiera' },
  { value: 'casual', label: 'Casual' },
  { value: 'trabajo', label: 'Trabajo' },
  { value: 'cena', label: 'Cena' },
  { value: 'fiesta', label: 'Fiesta' },
  { value: 'deportivo', label: 'Deportivo' },
] as const

const WEATHER_OPTIONS: ReadonlyArray<{
  value: OutfitWeather
  label: string
}> = [
  { value: 'frio', label: 'Frío' },
  { value: 'templado', label: 'Templado' },
  { value: 'calor', label: 'Calor' },
]

const EMPTY_WARDROBE: Garment[] = []

/** Mirrors the API `LayerFor`: base pieces, then outerwear, then accessories. */
function layerFor(garment: Garment): number {
  if (garment.category === 'outerwear') return 1
  if (garment.category === 'bag' || garment.category === 'accessory') return 2
  return 0
}

function draftKey(draft: OutfitDraft): string {
  return `${draft.name}|${[...draft.garmentIds].sort().join(',')}`
}

function quotaMessage(error: ApiError): string {
  if (error.retryAfterSeconds) {
    const minutes = Math.max(1, Math.ceil(error.retryAfterSeconds / 60))
    return `Se alcanzó la cuota diaria de sugerencias. Probá de nuevo en ${minutes} ${
      minutes === 1 ? 'minuto' : 'minutos'
    }.`
  }

  return 'Se alcanzó la cuota diaria de sugerencias. Probá de nuevo mañana.'
}

function errorMessage(caught: unknown): string {
  if (caught instanceof ApiError) {
    return caught.status === 429 ? quotaMessage(caught) : caught.message
  }

  return 'No pudimos generar sugerencias. Revisá tu conexión e intentá de nuevo.'
}

interface SuggestionPanelProps {
  onClose?: () => void
}

/**
 * AI suggestion flow. The wardrobe only unlocks the form; proposals stay in
 * React state until the user presses Guardar, which persists through the normal
 * create endpoint with source=ai and the proposal rationale.
 */
export function SuggestionPanel({ onClose }: SuggestionPanelProps) {
  const idPrefix = useId()
  const queryClient = useQueryClient()
  const [occasion, setOccasion] = useState('')
  const [weather, setWeather] = useState<OutfitWeather>('templado')
  const [resultWeather, setResultWeather] = useState<OutfitWeather>('templado')
  const [savedKeys, setSavedKeys] = useState<string[]>([])

  const wardrobeQuery = useQuery({
    queryKey: ['garments', 'outfit-suggestions'],
    queryFn: ({ signal }) => api.getAllGarments(5, signal),
    staleTime: 60 * 1000,
  })

  const wardrobe = wardrobeQuery.data ?? EMPTY_WARDROBE
  const wardrobeById = new Map(
    wardrobe.map((garment) => [garment.id, garment]),
  )
  const canMakeOutfit = hasPossibleOutfit(wardrobe)

  const saveMutation = useMutation({
    mutationFn: (draft: OutfitDraft) =>
      api.createOutfit({
        name: draft.name,
        occasion: draft.occasion,
        garmentIds: draft.garmentIds,
        source: 'ai',
        rationale: draft.rationale,
      }),
    onSuccess: (_saved, draft) => {
      setSavedKeys((current) => [...new Set([...current, draftKey(draft)])])
      void queryClient.invalidateQueries({ queryKey: ['outfits'] })
    },
  })

  const suggestMutation = useMutation({
    mutationFn: () =>
      api.suggestOutfits({
        occasion: occasion.trim() ? occasion.trim() : null,
        weather,
      }),
    onSuccess: () => {
      setSavedKeys([])
      // A successful generation starts a new proposal batch; a save error from
      // the previous batch must not linger next to the new cards.
      saveMutation.reset()
    },
  })

  const isFormDisabled =
    wardrobeQuery.isPending || wardrobeQuery.isError || !canMakeOutfit

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (isFormDisabled || suggestMutation.isPending) return
    // Remember the weather the successful proposals belong to, so changing the
    // select afterwards does not relabel the current cards.
    setResultWeather(weather)
    suggestMutation.mutate()
  }

  function handleSave(draft: OutfitDraft) {
    saveMutation.reset()
    saveMutation.mutate(draft)
  }

  function itemsFor(draft: OutfitDraft): OutfitItem[] {
    return draft.garmentIds
      .map((id) => wardrobeById.get(id))
      .filter((garment): garment is Garment => garment !== undefined)
      .map((garment, index) => ({
        garment,
        layer: layerFor(garment),
        sortOrder: index,
      }))
  }

  const drafts = suggestMutation.isSuccess
    ? (suggestMutation.data?.outfits ?? [])
    : []
  const suggestError = suggestMutation.error
  const suggestErrorIsQuota =
    suggestError instanceof ApiError && suggestError.status === 429

  return (
    <section
      aria-labelledby="suggestion-panel-title"
      className="rounded-2xl border border-stone-200 bg-white p-4 shadow-sm sm:p-6"
    >
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2
            id="suggestion-panel-title"
            className="font-display text-xl font-semibold tracking-tight text-stone-900"
          >
            Sugerencias de outfits
          </h2>
          <p className="mt-1 text-sm text-stone-500">
            Elegí una ocasión y el clima. La IA propone combinaciones con tus
            prendas; nada se guarda hasta que toques Guardar.
          </p>
        </div>
        {onClose && (
          <button
            type="button"
            onClick={onClose}
            className="rounded-xl border border-stone-300 bg-white px-3 py-1.5 text-sm font-medium text-stone-700 transition-colors hover:bg-stone-100"
          >
            Cerrar
          </button>
        )}
      </div>

      <form
        onSubmit={handleSubmit}
        className="mt-4 grid gap-3 sm:grid-cols-[1fr_1fr_auto] sm:items-end"
      >
        <label
          htmlFor={`${idPrefix}-occasion`}
          className="flex flex-col gap-1 text-xs font-medium text-stone-500"
        >
          Ocasión
          <select
            id={`${idPrefix}-occasion`}
            value={occasion}
            onChange={(event) => setOccasion(event.target.value)}
            disabled={isFormDisabled}
            className="rounded-xl border border-stone-300 bg-white px-3 py-2 text-sm text-stone-800 shadow-sm transition-colors focus:border-stone-500 focus:ring-2 focus:ring-stone-300 focus:outline-none disabled:cursor-not-allowed disabled:opacity-60"
          >
            {OCCASION_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </label>

        <label
          htmlFor={`${idPrefix}-weather`}
          className="flex flex-col gap-1 text-xs font-medium text-stone-500"
        >
          Clima
          <select
            id={`${idPrefix}-weather`}
            value={weather}
            onChange={(event) =>
              setWeather(event.target.value as OutfitWeather)
            }
            disabled={isFormDisabled}
            className="rounded-xl border border-stone-300 bg-white px-3 py-2 text-sm text-stone-800 shadow-sm transition-colors focus:border-stone-500 focus:ring-2 focus:ring-stone-300 focus:outline-none disabled:cursor-not-allowed disabled:opacity-60"
          >
            {WEATHER_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </label>

        <button
          type="submit"
          disabled={isFormDisabled || suggestMutation.isPending}
          className="rounded-xl bg-stone-900 px-4 py-2 text-sm font-medium text-white shadow-sm transition-colors hover:bg-stone-700 focus-visible:ring-2 focus-visible:ring-stone-400 focus-visible:ring-offset-2 focus-visible:outline-none disabled:cursor-not-allowed disabled:opacity-50"
        >
          {suggestMutation.isPending ? 'Generando…' : 'Generar'}
        </button>
      </form>

      {wardrobeQuery.isPending && (
        <p
          role="status"
          className="mt-4 rounded-xl border border-stone-200 bg-stone-50 p-4 text-sm text-stone-500"
        >
          Cargando tu guardarropa…
        </p>
      )}

      {wardrobeQuery.isError && (
        <div
          role="alert"
          className="mt-4 rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-900"
        >
          <p className="font-medium">No pudimos cargar tu guardarropa.</p>
          <p className="mt-1 text-red-800">
            {wardrobeQuery.error instanceof ApiError
              ? wardrobeQuery.error.message
              : 'Revisá tu conexión e intentá de nuevo.'}
          </p>
          <button
            type="button"
            onClick={() => void wardrobeQuery.refetch()}
            className="mt-3 rounded-xl border border-red-300 bg-white px-3 py-1.5 font-medium text-red-900 transition-colors hover:bg-red-100"
          >
            Reintentar
          </button>
        </div>
      )}

      {!wardrobeQuery.isPending &&
        !wardrobeQuery.isError &&
        !canMakeOutfit && (
          <p
            role="note"
            className="mt-4 rounded-xl border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-900"
          >
            {wardrobe.length === 0
              ? 'Tu guardarropa está vacío: subí prendas para recibir sugerencias.'
              : 'Necesitás al menos una parte superior y una inferior, o un vestido, más calzado para generar sugerencias.'}
          </p>
        )}

      {suggestMutation.isPending && (
        <div className="mt-5 space-y-3" aria-busy="true">
          <p role="status" className="text-sm text-stone-500">
            Buscando combinaciones…
          </p>
          <div className="grid gap-3 sm:grid-cols-2">
            {Array.from({ length: 2 }, (_, index) => (
              <div
                key={index}
                aria-hidden="true"
                className="h-48 animate-pulse rounded-2xl bg-stone-100"
              />
            ))}
          </div>
        </div>
      )}

      {suggestError && !suggestMutation.isPending && (
        <div
          role="alert"
          className={`mt-5 rounded-xl border p-4 text-sm ${
            suggestErrorIsQuota
              ? 'border-amber-200 bg-amber-50 text-amber-900'
              : 'border-red-200 bg-red-50 text-red-900'
          }`}
        >
          <p className="font-medium">
            {suggestErrorIsQuota
              ? 'Cuota diaria de sugerencias agotada'
              : 'No pudimos generar sugerencias.'}
          </p>
          <p className="mt-1">{errorMessage(suggestError)}</p>
          {!suggestErrorIsQuota && (
            <button
              type="button"
              onClick={() => suggestMutation.mutate()}
              className="mt-3 rounded-xl border border-red-300 bg-white px-3 py-1.5 font-medium text-red-900 transition-colors hover:bg-red-100"
            >
              Reintentar
            </button>
          )}
        </div>
      )}

      {!suggestMutation.isPending &&
        suggestMutation.isSuccess &&
        drafts.length === 0 && (
          <p
            role="status"
            className="mt-5 rounded-xl border border-dashed border-stone-300 bg-stone-50 px-4 py-8 text-center text-sm text-stone-500"
          >
            No encontramos combinaciones para esa ocasión y clima. Probá con
            otro clima u otra ocasión.
          </p>
        )}

      {drafts.length > 0 && !suggestMutation.isPending && (
        <div className="mt-5 space-y-3">
          <div className="flex flex-wrap items-center justify-between gap-2">
            <h3 className="font-display text-lg font-semibold tracking-tight text-stone-900">
              Outfit sugerido
            </h3>
            {savedKeys.length > 0 && (
              <p role="status" className="text-sm text-emerald-700">
                Outfit guardado en tu lista.
              </p>
            )}
          </div>

          <ul className="grid gap-3 sm:grid-cols-2">
            {drafts.map((draft) => {
              const key = draftKey(draft)
              const isSaved = savedKeys.includes(key)
              const isSaving =
                saveMutation.isPending &&
                saveMutation.variables !== undefined &&
                draftKey(saveMutation.variables) === key

              return (
                <li key={key}>
                  <article
                    data-testid="suggestion-card"
                    aria-label={draft.name}
                    className="flex h-full flex-col overflow-hidden rounded-2xl border border-stone-200 bg-white shadow-sm"
                  >
                    <OutfitCollage
                      items={itemsFor(draft)}
                      name={draft.name}
                    />
                    <div className="flex flex-1 flex-col gap-2 p-4">
                      <h4 className="font-medium text-stone-900">
                        {draft.name}
                      </h4>
                      <p className="text-sm text-stone-600">
                        {draft.rationale}
                      </p>
                      <p className="text-xs text-stone-400">
                        {draft.occasion
                          ? `Ocasión: ${draft.occasion}`
                          : 'Cualquier ocasión'}{' '}
                        · Clima:{' '}
                        {
                          WEATHER_OPTIONS.find(
                            (option) => option.value === resultWeather,
                          )?.label
                        }
                      </p>
                      <div className="mt-auto pt-2">
                        <button
                          type="button"
                          onClick={() => handleSave(draft)}
                          disabled={isSaved || saveMutation.isPending}
                          className="rounded-xl bg-stone-900 px-4 py-2 text-sm font-medium text-white shadow-sm transition-colors hover:bg-stone-700 focus-visible:ring-2 focus-visible:ring-stone-400 focus-visible:ring-offset-2 focus-visible:outline-none disabled:cursor-not-allowed disabled:opacity-50"
                        >
                          {isSaved
                            ? 'Guardado'
                            : isSaving
                              ? 'Guardando…'
                              : 'Guardar'}
                        </button>
                      </div>
                    </div>
                  </article>
                </li>
              )
            })}
          </ul>

          {saveMutation.error && (
            <p role="alert" className="text-sm text-red-700">
              {saveMutation.error instanceof ApiError
                ? saveMutation.error.message
                : 'No pudimos guardar el outfit. Revisá tu conexión e intentá de nuevo.'}
            </p>
          )}
        </div>
      )}
    </section>
  )
}
