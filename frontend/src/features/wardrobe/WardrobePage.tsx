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
    <button
      type="button"
      onClick={onClick}
      className="rounded-xl bg-stone-900 px-4 py-2 text-sm font-medium text-white shadow-sm transition-colors hover:bg-stone-700 focus-visible:ring-2 focus-visible:ring-stone-400 focus-visible:ring-offset-2 focus-visible:outline-none"
    >
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
    <section aria-labelledby="wardrobe-title" className="space-y-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1
            id="wardrobe-title"
            className="font-display text-3xl font-semibold tracking-tight text-stone-900 sm:text-4xl"
          >
            Guardarropa
          </h1>
          <p className="mt-1 text-sm text-stone-500">
            Todo lo que tenés, a mano y ordenado por categoría, color y
            temporada.
          </p>
          {isDemo && (
            <p
              role="note"
              className="mt-2 rounded-xl bg-amber-50 px-3 py-2 text-xs text-amber-900"
            >
              La demo pública es de solo lectura: no se pueden subir prendas.
            </p>
          )}
        </div>
        {!isDemo && <UploadButton onClick={() => setIsUploadOpen(true)} />}
      </div>

      <div className="rounded-2xl border border-stone-200 bg-white p-4 shadow-sm">
        <GarmentFilters
          filters={filters}
          onChange={setFilters}
          colorOptions={colorOptions}
        />
      </div>

      {garmentsQuery.isError && (
        <div
          role="alert"
          className="rounded-2xl border border-red-200 bg-red-50 p-5 text-sm text-red-900"
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
            className="mt-3 rounded-xl border border-red-300 bg-white px-3 py-1.5 font-medium text-red-900 transition-colors hover:bg-red-100"
          >
            Reintentar
          </button>
        </div>
      )}

      {!garmentsQuery.isError && (isInitialLoading || garments.length > 0) && (
        <div className="space-y-4">
          {!isInitialLoading && (
            <p className="text-xs text-stone-500" aria-live="polite">
              {garments.length === 1
                ? '1 prenda cargada'
                : `${garments.length} prendas cargadas`}
              {hasMore ? ' · hay más' : ''}
              {isRefreshing ? ' · actualizando…' : ''}
            </p>
          )}

          <ul
            aria-busy={isInitialLoading || isRefreshing || isFetchingMore}
            className={`grid grid-cols-2 gap-3 transition-opacity sm:gap-4 md:grid-cols-4 ${
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
                className="rounded-xl border border-stone-300 bg-white px-4 py-2 text-sm font-medium text-stone-800 shadow-sm transition-colors hover:bg-stone-100 disabled:cursor-not-allowed disabled:opacity-60"
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
          <div className="rounded-2xl border border-dashed border-stone-300 bg-white px-6 py-12 text-center">
            <HangerIcon className="mx-auto size-10 text-stone-300" />
            <h2 className="mt-4 text-base font-medium text-stone-900">
              No hay prendas con esos filtros
            </h2>
            <p className="mx-auto mt-1 max-w-sm text-sm text-stone-500">
              Probá con otra categoría, color o temporada, o volvé a ver todo tu
              guardarropa.
            </p>
            <button
              type="button"
              onClick={() => setFilters(EMPTY_FILTERS)}
              className="mt-4 rounded-xl border border-stone-300 bg-white px-4 py-2 text-sm font-medium text-stone-800 shadow-sm transition-colors hover:bg-stone-100"
            >
              Limpiar filtros
            </button>
          </div>
        ) : (
          <div className="rounded-2xl border border-dashed border-stone-300 bg-white px-6 py-12 text-center">
            <HangerIcon className="mx-auto size-10 text-stone-300" />
            <h2 className="mt-4 text-base font-medium text-stone-900">
              Tu guardarropa está vacío
            </h2>
            <p className="mx-auto mt-1 max-w-sm text-sm text-stone-500">
              {isDemo
                ? 'La demo pública es de solo lectura: podés explorar las prendas de ejemplo, pero no subir nuevas.'
                : 'Subí la foto de tu primera prenda y completá sus datos a mano.'}
            </p>
            {!isDemo && (
              <div className="mt-4 flex justify-center">
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
