import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { ApiError, api, mediaUrl } from '../../lib/api'
import type { Garment } from '../../lib/types'
import { categoryLabel, seasonLabel } from './garmentLabels'

interface GarmentCardProps {
  garment: Garment
  /** Demo mode: the delete action is not rendered. */
  readOnly?: boolean
}

function friendlyError(caught: unknown): string {
  return caught instanceof ApiError
    ? caught.message
    : 'No pudimos eliminar la prenda. Intentá de nuevo.'
}

export function GarmentCard({ garment, readOnly = false }: GarmentCardProps) {
  const queryClient = useQueryClient()
  const [isConfirmingDelete, setIsConfirmingDelete] = useState(false)
  const seasonText = garment.seasons.map(seasonLabel).join(' · ')

  const deleteMutation = useMutation({
    mutationFn: () => api.deleteGarment(garment.id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['garments'] })
    },
  })

  function handleConfirmDelete() {
    deleteMutation.reset()
    deleteMutation.mutate()
  }

  return (
    <article className="group flex h-full flex-col overflow-hidden rounded-2xl border border-stone-200 bg-white shadow-sm transition-shadow hover:shadow-md">
      <div className="relative aspect-square overflow-hidden bg-stone-100">
        <img
          src={mediaUrl(garment.thumbUrl)}
          alt={garment.name}
          width={400}
          height={400}
          loading="lazy"
          decoding="async"
          className="h-full w-full object-cover transition-transform duration-300 group-hover:scale-[1.02]"
        />
      </div>

      <div className="flex flex-1 flex-col gap-1.5 p-3 sm:p-4">
        <h2
          id={`garment-${garment.id}-name`}
          className="truncate text-sm font-medium text-stone-900"
          title={garment.name}
        >
          {garment.name}
        </h2>
        <p className="flex items-center gap-1.5 text-xs text-stone-600">
          <span
            aria-hidden="true"
            className="size-3 shrink-0 rounded-full border border-stone-300"
            style={{ backgroundColor: garment.colorHex }}
          />
          <span className="truncate">
            {garment.colorName} · {categoryLabel(garment.category)}
          </span>
        </p>
        <p className="truncate text-[11px] tracking-wide text-stone-400">
          {seasonText || '\u00a0'}
        </p>

        {!readOnly && (
          <div className="mt-auto pt-2">
            {isConfirmingDelete ? (
              <div className="rounded-xl bg-stone-50 p-2.5">
                <p className="text-xs text-stone-700">
                  ¿Eliminar «{garment.name}» del guardarropa?
                </p>
                <p className="mt-1 text-[11px] text-stone-500">
                  Los outfits guardados conservan su foto.
                </p>
                <div className="mt-2 flex gap-2">
                  <button
                    type="button"
                    onClick={handleConfirmDelete}
                    disabled={deleteMutation.isPending}
                    className="rounded-lg bg-red-600 px-2.5 py-1 text-xs font-medium text-white transition-colors hover:bg-red-700 disabled:cursor-not-allowed disabled:opacity-60"
                  >
                    {deleteMutation.isPending ? 'Eliminando…' : 'Sí, eliminar'}
                  </button>
                  <button
                    type="button"
                    onClick={() => setIsConfirmingDelete(false)}
                    disabled={deleteMutation.isPending}
                    className="rounded-lg border border-stone-300 bg-white px-2.5 py-1 text-xs font-medium text-stone-700 transition-colors hover:bg-stone-100 disabled:cursor-not-allowed disabled:opacity-60"
                  >
                    Cancelar
                  </button>
                </div>
              </div>
            ) : (
              <button
                type="button"
                onClick={() => setIsConfirmingDelete(true)}
                aria-label="Eliminar prenda"
                aria-describedby={`garment-${garment.id}-name`}
                className="text-xs font-medium text-stone-500 transition-colors hover:text-red-700"
              >
                Eliminar
              </button>
            )}

            {deleteMutation.error && (
              <p role="alert" className="mt-2 text-xs text-red-700">
                {friendlyError(deleteMutation.error)}
              </p>
            )}
          </div>
        )}
      </div>
    </article>
  )
}

/** Mirrors the card structure so the grid never jumps when data arrives. */
export function GarmentCardSkeleton() {
  return (
    <div
      aria-hidden="true"
      className="flex h-full flex-col overflow-hidden rounded-2xl border border-stone-200 bg-white"
    >
      <div className="aspect-square animate-pulse bg-stone-200" />
      <div className="flex flex-1 flex-col gap-2 p-3 sm:p-4">
        <div className="h-4 w-3/4 animate-pulse rounded bg-stone-200" />
        <div className="h-3 w-1/2 animate-pulse rounded bg-stone-200" />
        <div className="h-3 w-2/3 animate-pulse rounded bg-stone-100" />
        <div className="mt-2 h-3 w-1/4 animate-pulse rounded bg-stone-100" />
      </div>
    </div>
  )
}
