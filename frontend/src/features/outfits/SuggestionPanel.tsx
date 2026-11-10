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
    return `Demasiadas solicitudes. Probá de nuevo en ${minutes} ${
      minutes === 1 ? 'minuto' : 'minutos'
    }.`
  }

  return 'Demasiadas solicitudes. Probá de nuevo en un rato.'
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
      className="border border-stone-300/80 bg-white p-4 sm:p-6"
    >
      <div className="flex flex-wrap items-start justify-between gap-3 border-b border-stone-300/80 pb-4">
        <div>
          <p className="editorial-kicker">Motor de reglas</p>
          <h2
            id="suggestion-panel-title"
            className="mt-2 font-display text-3xl leading-none tracking-[-0.04em] text-ink"
          >
            Sugerencias de outfits
          </h2>
          <p className="mt-2 max-w-2xl font-serif text-sm leading-relaxed text-stone-600">
            Elegí una ocasión y el clima. El motor de reglas propone
            combinaciones con tus prendas; nada se guarda hasta que toques
            Guardar.
          </p>
        </div>
        {onClose && (
          <button
            type="button"
            onClick={onClose}
            className="editorial-btn-outline"
          >
            Cerrar
          </button>
        )}
      </div>

      <form
        onSubmit={handleSubmit}
        className="mt-5 grid gap-5 sm:grid-cols-[1fr_1fr_auto] sm:items-end"
      >
        <label
          htmlFor={`${idPrefix}-occasion`}
          className="flex flex-col gap-1"
        >
          <span className="editorial-label">Ocasión</span>
          <select
            id={`${idPrefix}-occasion`}
            value={occasion}
            onChange={(event) => setOccasion(event.target.value)}
            disabled={isFormDisabled}
            className="editorial-field"
          >
            {OCCASION_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </label>

        <label htmlFor={`${idPrefix}-weather`} className="flex flex-col gap-1">
          <span className="editorial-label">Clima</span>
          <select
            id={`${idPrefix}-weather`}
            value={weather}
            onChange={(event) =>
              setWeather(event.target.value as OutfitWeather)
            }
            disabled={isFormDisabled}
            className="editorial-field"
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
          className="editorial-btn"
        >
          {suggestMutation.isPending ? 'Generando…' : 'Generar'}
        </button>
      </form>

      {wardrobeQuery.isPending && (
        <p
          role="status"
          className="mt-5 border border-stone-300/80 bg-stone-50 p-4 font-serif text-sm text-stone-600"
        >
          Cargando tu guardarropa…
        </p>
      )}

      {wardrobeQuery.isError && (
        <div
          role="alert"
          className="mt-5 border border-red-200 bg-red-50 p-4 font-sans text-sm text-red-900"
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
            className="editorial-btn-outline mt-3 border-red-300 text-red-900 hover:bg-red-900"
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
            className="mt-5 border-l-2 border-sage bg-stone-50 py-2 pl-3 font-serif text-sm text-stone-700"
          >
            {wardrobe.length === 0
              ? 'Tu guardarropa está vacío: subí prendas para recibir sugerencias.'
              : 'Necesitás al menos una parte superior y una inferior, o un vestido, más calzado para generar sugerencias.'}
          </p>
        )}

      {suggestMutation.isPending && (
        <div className="mt-6 space-y-3" aria-busy="true">
          <p
            role="status"
            className="font-serif text-sm text-stone-600 italic"
          >
            Buscando combinaciones…
          </p>
          <div className="grid gap-4 sm:grid-cols-2">
            {Array.from({ length: 2 }, (_, index) => (
              <div
                key={index}
                aria-hidden="true"
                className="h-52 animate-pulse border border-stone-300/80 bg-stone-100"
              />
            ))}
          </div>
        </div>
      )}

      {suggestError && !suggestMutation.isPending && (
        <div
          role="alert"
          className={`mt-6 border p-4 font-sans text-sm ${
            suggestErrorIsQuota
              ? 'border-amber-300 bg-amber-50 text-amber-900'
              : 'border-red-200 bg-red-50 text-red-900'
          }`}
        >
          <p className="font-medium">
            {suggestErrorIsQuota
              ? 'Demasiadas solicitudes'
              : 'No pudimos generar sugerencias.'}
          </p>
          <p className="mt-1">{errorMessage(suggestError)}</p>
          {!suggestErrorIsQuota && (
            <button
              type="button"
              onClick={() => suggestMutation.mutate()}
              className="editorial-btn-outline mt-3 border-red-300 text-red-900 hover:bg-red-900"
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
            className="mt-6 border border-dashed border-stone-400 bg-stone-50 px-4 py-10 text-center font-serif text-sm text-stone-600"
          >
            No encontramos combinaciones para esa ocasión y clima. Probá con
            otro clima u otra ocasión.
          </p>
        )}

      {drafts.length > 0 && !suggestMutation.isPending && (
        <div className="mt-6 space-y-3">
          <div className="flex flex-wrap items-end justify-between gap-2 border-b border-stone-300/80 pb-2">
            <h3 className="font-display text-2xl leading-none tracking-[-0.03em] text-ink">
              Outfit sugerido
            </h3>
            {savedKeys.length > 0 && (
              <p role="status" className="font-serif text-sm text-sage">
                Outfit guardado en tu lista.
              </p>
            )}
          </div>

          <ul className="grid gap-4 sm:grid-cols-2">
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
                    className="flex h-full flex-col border border-stone-300/80 bg-white"
                  >
                    <OutfitCollage
                      items={itemsFor(draft)}
                      name={draft.name}
                    />
                    <div className="flex flex-1 flex-col gap-2 p-4">
                      <h4 className="font-display text-[22px] leading-tight tracking-[-0.02em] text-ink">
                        {draft.name}
                      </h4>
                      <p className="font-serif text-sm leading-relaxed text-stone-600">
                        {draft.rationale}
                      </p>
                      <p className="editorial-meta uppercase">
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
                      <div className="mt-auto pt-3">
                        <button
                          type="button"
                          onClick={() => handleSave(draft)}
                          disabled={isSaved || saveMutation.isPending}
                          className="editorial-btn"
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
            <p role="alert" className="font-serif text-sm text-red-700">
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
