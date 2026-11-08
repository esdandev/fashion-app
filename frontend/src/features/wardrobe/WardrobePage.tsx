import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { ApiError, api } from '../../lib/api'
import type { GarmentFiltersState } from '../../lib/types'
import { GarmentCard, GarmentCardSkeleton } from './GarmentCard'
import { GarmentFilters } from './GarmentFilters'
import { buildGarmentQuery } from './garmentQueryString'

const EMPTY_FILTERS: GarmentFiltersState = { category: '', season: '' }

function UploadButton() {
  return (
    <button
      type="button"
      disabled
      title="La carga de prendas llega en la próxima entrega."
      className="cursor-not-allowed rounded-xl bg-stone-900 px-4 py-2 text-sm font-medium text-white opacity-50"
    >
      Subir prenda
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
  const hasFilters = Boolean(filters.category || filters.season)

  const garmentsQuery = useQuery({
    queryKey: ['garments', filters],
    queryFn: ({ signal }) =>
      api.getGarments(buildGarmentQuery(filters), signal),
    placeholderData: keepPreviousData,
  })

  const garments = garmentsQuery.data?.items ?? []
  const hasMore = Boolean(garmentsQuery.data?.nextCursor)
  const isInitialLoading = garmentsQuery.isPending
  const isRefreshing = garmentsQuery.isPlaceholderData

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
            Todo lo que tenés, a mano y ordenado por categoría y temporada.
          </p>
        </div>
        <UploadButton />
      </div>

      <div className="rounded-2xl border border-stone-200 bg-white p-4 shadow-sm">
        <GarmentFilters filters={filters} onChange={setFilters} />
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
        <div className="space-y-3">
          {!isInitialLoading && (
            <p className="text-xs text-stone-500" aria-live="polite">
              {garments.length === 1
                ? '1 prenda'
                : `${garments.length} prendas`}
              {hasMore ? ' · mostrando las primeras' : ''}
              {isRefreshing ? ' · actualizando…' : ''}
            </p>
          )}

          <ul
            aria-busy={isInitialLoading || isRefreshing}
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
                    <GarmentCard garment={garment} />
                  </li>
                ))}
          </ul>
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
              Probá con otra categoría o temporada, o volvé a ver todo tu
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
              Subí la foto de tu primera prenda y la IA se encarga de completar
              categoría, color y temporada. La carga de fotos llega en la
              próxima entrega.
            </p>
            <div className="mt-4 flex justify-center">
              <UploadButton />
            </div>
          </div>
        ))}
    </section>
  )
}
