import { useQuery } from '@tanstack/react-query'
import { useMemo } from 'react'
import { Link } from 'react-router-dom'
import { ApiError, api, mediaUrl } from '../../lib/api'
import type { Garment } from '../../lib/types'
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
      strokeWidth="1.5"
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
    <div role="status" className="space-y-8">
      <span className="sr-only">Cargando el resumen de hoy…</span>
      <div aria-hidden="true" className="border-y border-stone-300/80 px-2 py-10">
        <div className="h-3 w-28 animate-pulse bg-stone-200" />
        <div className="mt-5 h-14 w-2/3 animate-pulse bg-stone-200" />
        <div className="mt-6 h-4 w-full max-w-prose animate-pulse bg-stone-100" />
        <div className="mt-2 h-4 w-3/4 max-w-prose animate-pulse bg-stone-100" />
      </div>
      <div aria-hidden="true" className="divide-y divide-stone-300/80">
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

  const relatedGarments = useMemo(() => {
    const seen = new Map<string, Garment>()
    for (const item of digest?.items ?? []) {
      for (const garment of item.relatedGarments) {
        if (!seen.has(garment.id)) seen.set(garment.id, garment)
      }
    }
    return [...seen.values()].slice(0, 4)
  }, [digest])

  return (
    <section aria-labelledby="today-title" className="space-y-9">
      <header className="grid items-end gap-5 border-b border-ink/60 pb-5 lg:grid-cols-[1fr_auto]">
        <div>
          <p className="editorial-kicker">Edición diaria</p>
          <h1 id="today-title" className="editorial-title mt-2">
            Hoy
          </h1>
        </div>
        <p className="max-w-sm font-serif text-[15px] leading-relaxed text-stone-600 italic lg:text-right">
          Lo que pasa en la moda, conectado con las prendas que ya tenés.
        </p>
      </header>

      {digestQuery.isPending && <TodaySkeleton />}

      {digestQuery.isError && !digest && (
        <div
          role="alert"
          className="border border-red-200 bg-red-50 p-5 font-sans text-sm text-red-900"
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
            className="editorial-btn-outline mt-4 border-red-300 text-red-900 hover:bg-red-900"
          >
            Reintentar
          </button>
        </div>
      )}

      {!digestQuery.isPending && !digestQuery.isError && !digest && (
        <div className="border border-dashed border-stone-400 px-6 py-14 text-center">
          <SunIcon className="mx-auto size-10 text-stone-400" />
          <h2 className="mt-4 text-xl text-ink">Todavía no hay resumen de hoy</h2>
          <p className="mx-auto mt-2 max-w-sm font-serif text-sm text-stone-600">
            El resumen se genera cada mañana. Mientras tanto, podés revisar tu
            guardarropa.
          </p>
          <Link to="/guardarropa" className="editorial-btn mt-5">
            Ver mi guardarropa
          </Link>
        </div>
      )}

      {digest && (
        <>
          <section
            className={`grid border-b border-ink/60 ${
              relatedGarments.length > 0 ? 'lg:grid-cols-[1.4fr_1fr]' : ''
            }`}
          >
            <div
              className={`py-7 ${
                relatedGarments.length > 0
                  ? 'lg:border-r lg:border-ink/25 lg:pr-10'
                  : ''
              }`}
            >
              <p className="editorial-kicker">Resumen del día</p>
              <h2 className="editorial-section-title mt-3">
                {formatDigestDate(digest.date)}
              </h2>
              <p className="mt-5 max-w-prose font-serif text-[17px] leading-relaxed text-stone-700">
                {digest.summary}
              </p>
              <p className="editorial-meta mt-5 uppercase">
                {digest.items.length === 1
                  ? '1 novedad'
                  : `${digest.items.length} novedades`}
              </p>
            </div>

            {relatedGarments.length > 0 && (
              <aside className="py-7 lg:pl-10">
                <p className="editorial-kicker">En tu guardarropa</p>
                <ul className="mt-4 space-y-2">
                  {relatedGarments.map((garment) => (
                    <li
                      key={garment.id}
                      className="flex items-center gap-3 border-b border-stone-300/60 pb-2"
                    >
                      <img
                        src={mediaUrl(garment.thumbUrl)}
                        alt=""
                        width={160}
                        height={160}
                        loading="lazy"
                        decoding="async"
                        className="size-12 shrink-0 border border-stone-300/80 bg-stone-100 object-cover"
                      />
                      <span className="min-w-0">
                        <span className="block truncate font-display text-[17px] leading-tight">
                          {garment.name}
                        </span>
                        <span className="editorial-meta block truncate uppercase">
                          {garment.colorName}
                        </span>
                      </span>
                    </li>
                  ))}
                </ul>
              </aside>
            )}
          </section>

          {digest.items.length > 0 ? (
            <section>
              <div className="flex items-baseline justify-between gap-4 border-b border-ink pb-2">
                <h2 className="editorial-section-title text-3xl sm:text-4xl">
                  Despacho de moda
                </h2>
                <span className="editorial-meta uppercase">
                  {digest.items.length === 1
                    ? 'Una lectura'
                    : `${digest.items.length} lecturas`}
                </span>
              </div>
              <ul
                aria-label="Novedades del día"
                className="divide-y divide-stone-300/80"
              >
                {digest.items.map((item, index) => (
                  <li key={item.id} className="min-w-0">
                    <DigestCard
                      item={item}
                      index={index}
                      priority={index === 0}
                    />
                  </li>
                ))}
              </ul>
            </section>
          ) : (
            <div className="border border-dashed border-stone-400 px-6 py-12 text-center font-serif text-sm text-stone-600">
              Todavía no hay novedades para hoy.
            </div>
          )}
        </>
      )}
    </section>
  )
}
