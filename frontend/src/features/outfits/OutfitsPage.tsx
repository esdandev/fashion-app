import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { ApiError, api } from '../../lib/api'
import type { Outfit } from '../../lib/types'
import { OutfitCard, OutfitCardSkeleton } from './OutfitCard'
import { OutfitEditor } from './OutfitEditor'
import { SuggestionPanel } from './SuggestionPanel'

function CollageIcon({ className }: { className?: string }) {
  return (
    <svg
      viewBox="0 0 48 48"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      className={className}
    >
      <rect x="7" y="8" width="16" height="16" rx="2" />
      <rect x="27" y="8" width="14" height="10" rx="2" />
      <rect x="27" y="22" width="14" height="18" rx="2" />
      <rect x="7" y="28" width="16" height="12" rx="2" />
    </svg>
  )
}

export default function OutfitsPage() {
  const [editor, setEditor] = useState<{ outfit?: Outfit } | null>(null)
  const [showSuggestions, setShowSuggestions] = useState(false)

  const outfitsQuery = useQuery({
    queryKey: ['outfits'],
    queryFn: ({ signal }) => api.getOutfits(signal),
  })

  const configQuery = useQuery({
    queryKey: ['config'],
    queryFn: () => api.getConfig(),
    staleTime: 5 * 60 * 1000,
  })

  const isDemo = configQuery.data?.demo === true
  const outfits = outfitsQuery.data ?? []
  const isInitialLoading = outfitsQuery.isPending

  return (
    <section aria-labelledby="outfits-title" className="space-y-8">
      <div className="grid items-end gap-5 border-b border-ink/60 pb-5 lg:grid-cols-[1fr_auto]">
        <div>
          <p className="editorial-kicker">Composiciones</p>
          <h1 id="outfits-title" className="editorial-title mt-2">
            Outfits
          </h1>
          <p className="mt-4 max-w-xl font-serif text-[15px] leading-relaxed text-stone-600">
            Combinaciones armadas con tus prendas reales.
          </p>
          {isDemo && (
            <p
              role="note"
              className="mt-3 max-w-xl border-l-2 border-oxblood pl-3 font-serif text-xs text-stone-600"
            >
              La demo pública es de solo lectura: podés explorar los outfits de
              ejemplo, pero no modificarlos.
            </p>
          )}
        </div>
        {!isDemo && (
          <div className="flex flex-wrap gap-2">
            <button
              type="button"
              onClick={() => {
                setEditor(null)
                setShowSuggestions(true)
              }}
              className="editorial-btn"
            >
              Sugerir outfits
            </button>
            <button
              type="button"
              onClick={() => {
                setShowSuggestions(false)
                setEditor({})
              }}
              className="editorial-btn-outline"
            >
              Crear outfit
            </button>
          </div>
        )}
      </div>

      {showSuggestions && (
        <SuggestionPanel onClose={() => setShowSuggestions(false)} />
      )}

      {editor && (
        <OutfitEditor
          key={editor.outfit?.id ?? 'nuevo'}
          outfit={editor.outfit}
          onClose={() => setEditor(null)}
        />
      )}

      {outfitsQuery.isError && (
        <div
          role="alert"
          className="border border-red-200 bg-red-50 p-5 font-sans text-sm text-red-900"
        >
          <p className="font-medium">No pudimos cargar los outfits.</p>
          <p className="mt-1 text-red-800">
            {outfitsQuery.error instanceof ApiError
              ? outfitsQuery.error.message
              : 'Revisá tu conexión e intentá de nuevo.'}
          </p>
          <button
            type="button"
            onClick={() => void outfitsQuery.refetch()}
            className="editorial-btn-outline mt-4 border-red-300 text-red-900 hover:bg-red-900"
          >
            Reintentar
          </button>
        </div>
      )}

      {!outfitsQuery.isError && (isInitialLoading || outfits.length > 0) && (
        <div className="space-y-4">
          {!isInitialLoading && (
            <p className="editorial-meta uppercase" aria-live="polite">
              {outfits.length === 1
                ? '1 outfit guardado'
                : `${outfits.length} outfits guardados`}
            </p>
          )}

          <ul
            aria-busy={isInitialLoading}
            className="grid gap-px border border-stone-300/80 bg-stone-300/80 sm:grid-cols-2 xl:grid-cols-3"
          >
            {isInitialLoading
              ? Array.from({ length: 6 }, (_, index) => (
                  <li key={index} aria-hidden="true">
                    <OutfitCardSkeleton />
                  </li>
                ))
              : outfits.map((outfit) => (
                  <li key={outfit.id}>
                    <OutfitCard
                      outfit={outfit}
                      readOnly={isDemo}
                      onEdit={(selected) => setEditor({ outfit: selected })}
                    />
                  </li>
                ))}
          </ul>
        </div>
      )}

      {!outfitsQuery.isError &&
        !isInitialLoading &&
        outfits.length === 0 && (
          <div className="border border-dashed border-stone-400 px-6 py-14 text-center">
            <CollageIcon className="mx-auto size-10 text-stone-400" />
            <h2 className="mt-4 text-xl text-ink">
              Todavía no guardaste outfits
            </h2>
            <p className="mx-auto mt-2 max-w-sm font-serif text-sm text-stone-600">
              {isDemo
                ? 'La demo pública es de solo lectura: podés explorar el guardarropa de ejemplo, pero no crear outfits.'
                : 'Cuando guardes uno, va a aparecer acá como un collage de tus prendas reales.'}
            </p>
            <Link to="/guardarropa" className="editorial-btn mt-5">
              Ir al guardarropa
            </Link>
          </div>
        )}
    </section>
  )
}
