import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { ApiError, api } from '../../lib/api'
import { DigestCard, DigestCardSkeleton } from './DigestCard'
import { formatDigestDate } from './digestFormat'

/** Matches the API `Cache-Control: private, max-age=300`. */
const DIGEST_STALE_TIME = 5 * 60 * 1000

function SunIcon({ className }: { className?: string }) {
  return (
    <svg
      viewBox="0 0 48 48"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      aria-hidden="true"
      className={className}
    >
      <circle cx="24" cy="24" r="8" />
      <path d="M24 6v5M24 37v5M6 24h5M37 24h5M11.3 11.3l3.5 3.5M33.2 33.2l3.5 3.5M36.7 11.3l-3.5 3.5M14.8 33.2l-3.5 3.5" />
    </svg>
  )
}

function TodaySkeleton() {
  return (
    <div role="status" className="space-y-6">
      <span className="sr-only">Cargando el resumen de hoy…</span>
      <div
        aria-hidden="true"
        className="rounded-3xl bg-stone-900 px-6 py-8 sm:px-10 sm:py-10"
      >
        <div className="h-3 w-24 animate-pulse rounded bg-white/20" />
        <div className="mt-3 h-8 w-2/3 animate-pulse rounded bg-white/15" />
        <div className="mt-4 h-4 w-full max-w-prose animate-pulse rounded bg-white/10" />
        <div className="mt-2 h-4 w-3/4 max-w-prose animate-pulse rounded bg-white/10" />
        <div className="mt-5 h-5 w-28 animate-pulse rounded-full bg-white/10" />
      </div>
      <div
        aria-hidden="true"
        className="grid grid-cols-1 gap-4 md:grid-cols-2"
      >
        {Array.from({ length: 4 }, (_, index) => (
          <DigestCardSkeleton key={index} />
        ))}
      </div>
    </div>
  )
}

export default function TodayPage() {
  const digestQuery = useQuery({
    queryKey: ['digest', 'today'],
    queryFn: async ({ signal }) => {
      try {
        return await api.getDigest(signal)
      } catch (error) {
        // Not having a digest yet is an empty state, not a failure.
        if (error instanceof ApiError && error.status === 404) {
          return null
        }

        throw error
      }
    },
    staleTime: DIGEST_STALE_TIME,
  })

  const digest = digestQuery.data

  return (
    <section aria-labelledby="today-title" className="space-y-6">
      <div>
        <h1
          id="today-title"
          className="font-display text-3xl font-semibold tracking-tight text-stone-900 sm:text-4xl"
        >
          Hoy
        </h1>
        <p className="mt-1 text-sm text-stone-500">
          Lo que pasa en la moda, conectado con las prendas que ya tenés.
        </p>
      </div>

      {digestQuery.isPending && <TodaySkeleton />}

      {digestQuery.isError && !digest && (
        <div
          role="alert"
          className="rounded-2xl border border-red-200 bg-red-50 p-5 text-sm text-red-900"
        >
          <p className="font-medium">No pudimos cargar el resumen de hoy.</p>
          <p className="mt-1 text-red-800">
            {digestQuery.error instanceof ApiError
              ? digestQuery.error.message
              : 'Revisá tu conexión e intentá de nuevo.'}
          </p>
          <button
            type="button"
            onClick={() => void digestQuery.refetch()}
            className="mt-3 rounded-xl border border-red-300 bg-white px-3 py-1.5 font-medium text-red-900 transition-colors hover:bg-red-100"
          >
            Reintentar
          </button>
        </div>
      )}

      {!digestQuery.isPending && !digestQuery.isError && !digest && (
        <div className="rounded-2xl border border-dashed border-stone-300 bg-white px-6 py-12 text-center">
          <SunIcon className="mx-auto size-10 text-stone-300" />
          <h2 className="mt-4 text-base font-medium text-stone-900">
            Todavía no hay resumen de hoy
          </h2>
          <p className="mx-auto mt-1 max-w-sm text-sm text-stone-500">
            El resumen se genera cada mañana. Mientras tanto, podés revisar tu
            guardarropa.
          </p>
          <Link
            to="/guardarropa"
            className="mt-4 inline-flex rounded-xl bg-stone-900 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-stone-700"
          >
            Ver mi guardarropa
          </Link>
        </div>
      )}

      {digest && (
        <>
          <header className="rounded-3xl bg-stone-900 px-6 py-8 text-white shadow-sm sm:px-10 sm:py-10">
            <p className="text-[11px] font-medium tracking-[0.2em] text-stone-400 uppercase">
              Resumen del día
            </p>
            <h2 className="font-display mt-2 text-2xl font-semibold tracking-tight sm:text-3xl">
              {formatDigestDate(digest.date)}
            </h2>
            <p className="mt-3 max-w-prose text-sm leading-relaxed text-stone-300 sm:text-base">
              {digest.summary}
            </p>
            <p className="mt-5 inline-flex rounded-full bg-white/10 px-3 py-1 text-xs font-medium text-stone-200">
              {digest.items.length === 1
                ? '1 novedad'
                : `${digest.items.length} novedades`}
            </p>
          </header>

          {digest.items.length > 0 ? (
            <ul
              aria-label="Novedades del día"
              className="grid grid-cols-1 gap-4 md:grid-cols-2"
            >
              {digest.items.map((item, index) => (
                <li key={item.id} className="min-w-0">
                  <DigestCard item={item} priority={index === 0} />
                </li>
              ))}
            </ul>
          ) : (
            <div className="rounded-2xl border border-dashed border-stone-300 bg-white px-6 py-12 text-center text-sm text-stone-500">
              Todavía no hay novedades para hoy.
            </div>
          )}
        </>
      )}
    </section>
  )
}
