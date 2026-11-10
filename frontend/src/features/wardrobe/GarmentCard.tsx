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
    <article className="group flex h-full flex-col bg-white">
      <div className="relative aspect-square overflow-hidden bg-stone-100">
        <img
          src={mediaUrl(garment.thumbUrl)}
          alt={garment.name}
          width={400}
          height={400}
          loading="lazy"
          decoding="async"
          className="h-full w-full object-cover transition-transform duration-500 group-hover:scale-[1.03]"
        />
      </div>

      <div className="flex flex-1 flex-col p-3.5 sm:p-4">
        <h2
          id={`garment-${garment.id}-name`}
          className="font-display text-[21px] leading-[1.08] tracking-[-0.02em] text-ink"
          title={garment.name}
        >
          {garment.name}
        </h2>
        <p className="mt-2 flex items-center gap-1.5 font-sans text-[11px] tracking-[0.06em] text-stone-600 uppercase">
          <span
            aria-hidden="true"
            className="size-2.5 shrink-0 rounded-full border border-stone-400"
            style={{ backgroundColor: garment.colorHex }}
          />
          <span className="truncate">
            {garment.colorName} · {categoryLabel(garment.category)}
          </span>
        </p>
        <p className="mt-1 truncate font-sans text-[10px] tracking-[0.08em] text-stone-500 uppercase">
          {seasonText || '\u00a0'}
        </p>

        {!readOnly && (
          <div className="mt-auto pt-3">
            {isConfirmingDelete ? (
              <div className="border-t border-stone-300 pt-2.5">
                <p className="font-serif text-xs text-stone-700">
                  ¿Eliminar «{garment.name}» del guardarropa?
                </p>
                <p className="mt-1 font-serif text-[11px] text-stone-500">
                  Los outfits guardados conservan su foto.
                </p>
                <div className="mt-2 flex gap-2">
                  <button
                    type="button"
                    onClick={handleConfirmDelete}
                    disabled={deleteMutation.isPending}
                    className="border border-red-800 bg-red-800 px-2.5 py-1 font-sans text-[10px] font-semibold tracking-[0.08em] text-white uppercase transition-colors hover:bg-red-900 disabled:cursor-not-allowed disabled:opacity-60"
                  >
                    {deleteMutation.isPending ? 'Eliminando…' : 'Sí, eliminar'}
                  </button>
                  <button
                    type="button"
                    onClick={() => setIsConfirmingDelete(false)}
                    disabled={deleteMutation.isPending}
                    className="border border-stone-400 px-2.5 py-1 font-sans text-[10px] font-semibold tracking-[0.08em] text-stone-700 uppercase transition-colors hover:border-ink disabled:cursor-not-allowed disabled:opacity-60"
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
                className="editorial-link text-stone-500"
              >
                Eliminar
              </button>
            )}

            {deleteMutation.error && (
              <p role="alert" className="mt-2 font-serif text-xs text-red-700">
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
    <div aria-hidden="true" className="flex h-full flex-col bg-white">
      <div className="aspect-square animate-pulse bg-stone-200" />
      <div className="flex flex-1 flex-col p-3.5 sm:p-4">
        <div className="h-5 w-3/4 animate-pulse bg-stone-200" />
        <div className="mt-2 h-3 w-1/2 animate-pulse bg-stone-100" />
        <div className="mt-1.5 h-3 w-2/3 animate-pulse bg-stone-100" />
        <div className="mt-4 h-3 w-1/4 animate-pulse bg-stone-100" />
      </div>
    </div>
  )
}
