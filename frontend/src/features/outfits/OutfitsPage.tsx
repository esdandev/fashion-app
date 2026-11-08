import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { ApiError, api } from '../../lib/api'
import { OutfitCard, OutfitCardSkeleton } from './OutfitCard'

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
    <section aria-labelledby="outfits-title" className="space-y-6">
      <div>
        <h1
          id="outfits-title"
          className="font-display text-3xl font-semibold tracking-tight text-stone-900 sm:text-4xl"
        >
          Outfits
        </h1>
        <p className="mt-1 text-sm text-stone-500">
          Combinaciones armadas con tus prendas reales.
        </p>
        {isDemo && (
          <p
            role="note"
            className="mt-2 rounded-xl bg-amber-50 px-3 py-2 text-xs text-amber-900"
          >
            La demo pública es de solo lectura: podés explorar los outfits de
            ejemplo, pero no modificarlos.
          </p>
        )}
      </div>

      {outfitsQuery.isError && (
        <div
          role="alert"
          className="rounded-2xl border border-red-200 bg-red-50 p-5 text-sm text-red-900"
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
            className="mt-3 rounded-xl border border-red-300 bg-white px-3 py-1.5 font-medium text-red-900 transition-colors hover:bg-red-100"
          >
            Reintentar
          </button>
        </div>
      )}

      {!outfitsQuery.isError && (isInitialLoading || outfits.length > 0) && (
        <div className="space-y-4">
          {!isInitialLoading && (
            <p className="text-xs text-stone-500" aria-live="polite">
              {outfits.length === 1
                ? '1 outfit guardado'
                : `${outfits.length} outfits guardados`}
            </p>
          )}

          <ul
            aria-busy={isInitialLoading}
            className="grid gap-3 sm:grid-cols-2 sm:gap-4 xl:grid-cols-3"
          >
            {isInitialLoading
              ? Array.from({ length: 6 }, (_, index) => (
                  <li key={index} aria-hidden="true">
                    <OutfitCardSkeleton />
                  </li>
                ))
              : outfits.map((outfit) => (
                  <li key={outfit.id}>
                    <OutfitCard outfit={outfit} readOnly={isDemo} />
                  </li>
                ))}
          </ul>
        </div>
      )}

      {!outfitsQuery.isError &&
        !isInitialLoading &&
        outfits.length === 0 && (
          <div className="rounded-2xl border border-dashed border-stone-300 bg-white px-6 py-12 text-center">
            <CollageIcon className="mx-auto size-10 text-stone-300" />
            <h2 className="mt-4 text-base font-medium text-stone-900">
              Todavía no guardaste outfits
            </h2>
            <p className="mx-auto mt-1 max-w-sm text-sm text-stone-500">
              {isDemo
                ? 'La demo pública es de solo lectura: podés explorar el guardarropa de ejemplo, pero no crear outfits.'
                : 'Cuando guardes uno, va a aparecer acá como un collage de tus prendas reales.'}
            </p>
            <Link
              to="/guardarropa"
              className="mt-4 inline-flex rounded-xl bg-stone-900 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-stone-700"
            >
              Ir al guardarropa
            </Link>
          </div>
        )}
    </section>
  )
}
