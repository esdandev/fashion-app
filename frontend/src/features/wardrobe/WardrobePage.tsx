import {
  keepPreviousData,
  useInfiniteQuery,
  useQuery,
  useQueryClient,
} from '@tanstack/react-query'
import { useMemo, useState } from 'react'
import { ApiError, api } from '../../lib/api'
import type { GarmentFiltersState } from '../../lib/types'
import { useDebouncedValue } from '../../lib/useDebouncedValue'
import { GarmentCard, GarmentCardSkeleton } from './GarmentCard'
import { GarmentFilters } from './GarmentFilters'
import { buildGarmentQuery } from './garmentQueryString'
import { UploadGarmentDialog } from './UploadGarmentDialog'

const EMPTY_FILTERS: GarmentFiltersState = {
  category: '',
  color: '',
  season: '',
}

function UploadButton({ onClick }: { onClick: () => void }) {
  return (
    <button type="button" onClick={onClick} className="editorial-btn">
      Subir prendas
    </button>
  )
}

function HangerIcon({ className }: { className?: string }) {
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
      <path d="M24 14a4.5 4.5 0 1 1 4 4.5c-1.6.3-2.5 1.2-2.5 2.7v1.3l13 8.5c1.5 1 .9 3.5-1 3.5H10.5c-1.9 0-2.5-2.5-1-3.5L22.5 22" />
    </svg>
  )
}

export default function WardrobePage() {
  const [filters, setFilters] = useState<GarmentFiltersState>(EMPTY_FILTERS)
  const [isUploadOpen, setIsUploadOpen] = useState(false)
  const queryClient = useQueryClient()

  // Only the free-text color waits; selects apply immediately.
  const debouncedColor = useDebouncedValue(filters.color, 300)
  const appliedFilters = useMemo(
    () => ({ ...filters, color: debouncedColor }),
    [filters, debouncedColor],
  )
  const hasFilters = Boolean(filters.category || filters.color || filters.season)

  const garmentsQuery = useInfiniteQuery({
    queryKey: ['garments', appliedFilters],
    queryFn: ({ pageParam, signal }) =>
      api.getGarments(
        buildGarmentQuery({ ...appliedFilters, cursor: pageParam }),
        signal,
      ),
    initialPageParam: '',
    getNextPageParam: (lastPage) => lastPage.nextCursor ?? undefined,
    placeholderData: keepPreviousData,
  })

  const configQuery = useQuery({
    queryKey: ['config'],
    queryFn: () => api.getConfig(),
    staleTime: 5 * 60 * 1000,
  })

  const isDemo = configQuery.data?.demo === true
  const garments = useMemo(
    () => garmentsQuery.data?.pages.flatMap((page) => page.items) ?? [],
    [garmentsQuery.data],
  )
  const colorOptions = useMemo(() => {
    const seen = new Map<string, string>()
    for (const garment of garments) {
      const color = garment.colorName.trim()
      if (color && !seen.has(color.toLowerCase())) {
        seen.set(color.toLowerCase(), color)
      }
    }

    return [...seen.values()].sort((left, right) =>
      left.localeCompare(right, 'es'),
    )
  }, [garments])

  const hasMore = garmentsQuery.hasNextPage
  const isInitialLoading = garmentsQuery.isPending
  const isFetchingMore = garmentsQuery.isFetchingNextPage
  const isRefreshing =
    garmentsQuery.isPlaceholderData && !garmentsQuery.isFetchingNextPage

  return (
    <section aria-labelledby="wardrobe-title" className="space-y-8">
      <div className="grid items-end gap-5 border-b border-ink/60 pb-5 lg:grid-cols-[1fr_auto]">
        <div>
          <p className="editorial-kicker">El guardarropa</p>
          <h1 id="wardrobe-title" className="editorial-title mt-2">
            Guardarropa
          </h1>
          <p className="mt-4 max-w-xl font-serif text-[15px] leading-relaxed text-stone-600">
            Todo lo que tenés, a mano y ordenado por categoría, color y
            temporada.
          </p>
          {isDemo && (
            <p
              role="note"
              className="mt-3 max-w-xl border-l-2 border-oxblood pl-3 font-serif text-xs text-stone-600"
            >
              La demo pública es de solo lectura: no se pueden subir prendas.
            </p>
          )}
        </div>
        {!isDemo && <UploadButton onClick={() => setIsUploadOpen(true)} />}
      </div>

      <div className="border-y border-stone-300/80 py-3">
        <GarmentFilters
          filters={filters}
          onChange={setFilters}
          colorOptions={colorOptions}
        />
      </div>

      {garmentsQuery.isError && (
        <div
          role="alert"
          className="border border-red-200 bg-red-50 p-5 font-sans text-sm text-red-900"
        >
          <p className="font-medium">No pudimos cargar el guardarropa.</p>
          <p className="mt-1 text-red-800">
            {garmentsQuery.error instanceof ApiError
              ? garmentsQuery.error.message
              : 'Revisá tu conexión e intentá de nuevo.'}
          </p>
          <button
            type="button"
            onClick={() => void garmentsQuery.refetch()}
            className="editorial-btn-outline mt-4 border-red-300 text-red-900 hover:bg-red-900"
          >
            Reintentar
          </button>
        </div>
      )}

      {!garmentsQuery.isError && (isInitialLoading || garments.length > 0) && (
        <div className="space-y-4">
          {!isInitialLoading && (
            <p className="editorial-meta uppercase" aria-live="polite">
              {garments.length === 1
                ? '1 prenda cargada'
                : `${garments.length} prendas cargadas`}
              {hasMore ? ' · hay más' : ''}
              {isRefreshing ? ' · actualizando…' : ''}
            </p>
          )}

          <ul
            aria-busy={isInitialLoading || isRefreshing || isFetchingMore}
            className={`grid grid-cols-2 gap-px border border-stone-300/80 bg-stone-300/80 transition-opacity md:grid-cols-4 ${
              isRefreshing ? 'opacity-60' : ''
            }`}
          >
            {isInitialLoading
              ? Array.from({ length: 8 }, (_, index) => (
                  <li key={index} aria-hidden="true">
                    <GarmentCardSkeleton />
                  </li>
                ))
              : garments.map((garment) => (
                  <li key={garment.id}>
                    <GarmentCard garment={garment} readOnly={isDemo} />
                  </li>
                ))}
          </ul>

          {hasMore && !isInitialLoading && !isRefreshing && (
            <div className="flex justify-center">
              <button
                type="button"
                onClick={() => void garmentsQuery.fetchNextPage()}
                disabled={isFetchingMore}
                className="editorial-btn-outline"
              >
                {isFetchingMore ? 'Cargando…' : 'Cargar más'}
              </button>
            </div>
          )}
        </div>
      )}

      {!garmentsQuery.isError &&
        !isInitialLoading &&
        garments.length === 0 &&
        (hasFilters ? (
          <div className="border border-dashed border-stone-400 px-6 py-14 text-center">
            <HangerIcon className="mx-auto size-10 text-stone-400" />
            <h2 className="mt-4 text-xl text-ink">
              No hay prendas con esos filtros
            </h2>
            <p className="mx-auto mt-2 max-w-sm font-serif text-sm text-stone-600">
              Probá con otra categoría, color o temporada, o volvé a ver todo tu
              guardarropa.
            </p>
            <button
              type="button"
              onClick={() => setFilters(EMPTY_FILTERS)}
              className="editorial-btn-outline mt-5"
            >
              Limpiar filtros
            </button>
          </div>
        ) : (
          <div className="border border-dashed border-stone-400 px-6 py-14 text-center">
            <HangerIcon className="mx-auto size-10 text-stone-400" />
            <h2 className="mt-4 text-xl text-ink">
              Tu guardarropa está vacío
            </h2>
            <p className="mx-auto mt-2 max-w-sm font-serif text-sm text-stone-600">
              {isDemo
                ? 'La demo pública es de solo lectura: podés explorar las prendas de ejemplo, pero no subir nuevas.'
                : 'Subí la foto de tu primera prenda y completá sus datos a mano.'}
            </p>
            {!isDemo && (
              <div className="mt-5 flex justify-center">
                <UploadButton onClick={() => setIsUploadOpen(true)} />
              </div>
            )}
          </div>
        ))}

      {isUploadOpen && (
        <UploadGarmentDialog
          onClose={() => setIsUploadOpen(false)}
          onSaved={() => {
            setIsUploadOpen(false)
            void queryClient.invalidateQueries({ queryKey: ['garments'] })
          }}
        />
      )}
    </section>
  )
}
