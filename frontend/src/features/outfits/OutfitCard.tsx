import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { ApiError, api } from '../../lib/api'
import type { Outfit } from '../../lib/types'
import { OutfitCollage } from './OutfitCollage'

interface OutfitCardProps {
  outfit: Outfit
  /** Demo mode: no favorite, edit or delete actions are rendered. */
  readOnly?: boolean
  /** Opens the builder prefilled with this outfit. */
  onEdit?: (outfit: Outfit) => void
}

function HeartIcon({ filled }: { filled: boolean }) {
  return (
    <svg
      viewBox="0 0 24 24"
      fill={filled ? 'currentColor' : 'none'}
      stroke="currentColor"
      strokeWidth="1.6"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      className="size-4"
    >
      <path d="M12 20.5 4.7 13.2a4.8 4.8 0 0 1 0-6.8 4.8 4.8 0 0 1 6.8 0l.5.5.5-.5a4.8 4.8 0 0 1 6.8 0 4.8 4.8 0 0 1 0 6.8Z" />
    </svg>
  )
}

function friendlyError(caught: unknown, fallback: string): string {
  return caught instanceof ApiError ? caught.message : fallback
}

/**
 * One saved outfit: collage, name and occasion, plus the favorite and delete
 * actions. Mutations invalidate the `['outfits']` query so the list keeps the
 * server ordering (favorites first) and never shows optimistic destructive UI.
 */
export function OutfitCard({
  outfit,
  readOnly = false,
  onEdit,
}: OutfitCardProps) {
  const queryClient = useQueryClient()
  const [isConfirmingDelete, setIsConfirmingDelete] = useState(false)

  function invalidateOutfits() {
    void queryClient.invalidateQueries({ queryKey: ['outfits'] })
  }

  const favoriteMutation = useMutation({
    mutationFn: () =>
      api.patchOutfit(outfit.id, { isFavorite: !outfit.isFavorite }),
    onSuccess: invalidateOutfits,
  })

  const deleteMutation = useMutation({
    mutationFn: () => api.deleteOutfit(outfit.id),
    onSuccess: invalidateOutfits,
  })

  const actionError = favoriteMutation.error ?? deleteMutation.error
  const itemCount = outfit.items.length
  const occasion = outfit.occasion?.trim()

  function handleToggleFavorite() {
    favoriteMutation.reset()
    favoriteMutation.mutate()
  }

  function handleConfirmDelete() {
    deleteMutation.reset()
    deleteMutation.mutate()
  }

  return (
    <article className="group flex h-full flex-col bg-white">
      <OutfitCollage items={outfit.items} name={outfit.name} />

      <div className="flex flex-1 flex-col gap-3 p-4">
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0">
            <h2
              className="font-display text-[24px] leading-[1.02] tracking-[-0.03em] text-ink"
              title={outfit.name}
            >
              {outfit.name}
            </h2>
            <p className="editorial-meta mt-1 uppercase">
              {occasion || 'Sin ocasión'}
            </p>
          </div>

          {!readOnly && (
            <button
              type="button"
              onClick={handleToggleFavorite}
              disabled={favoriteMutation.isPending}
              aria-pressed={outfit.isFavorite}
              aria-label={
                outfit.isFavorite
                  ? 'Quitar de favoritos'
                  : 'Marcar como favorito'
              }
              title={
                outfit.isFavorite
                  ? 'Quitar de favoritos'
                  : 'Marcar como favorito'
              }
              className={`shrink-0 border p-1.5 transition-colors focus-visible:outline-none disabled:cursor-not-allowed disabled:opacity-50 ${
                outfit.isFavorite
                  ? 'border-oxblood text-oxblood hover:bg-oxblood hover:text-paper'
                  : 'border-stone-400 text-stone-500 hover:border-ink hover:text-ink'
              }`}
            >
              <HeartIcon filled={outfit.isFavorite} />
            </button>
          )}
        </div>

        <p className="editorial-meta uppercase">
          {itemCount === 1 ? '1 prenda' : `${itemCount} prendas`}
        </p>

        {!readOnly && (
          <div className="mt-auto pt-3">
            {isConfirmingDelete ? (
              <div className="border-t border-stone-300 pt-2.5">
                <p className="font-serif text-xs text-stone-700">
                  ¿Eliminar «{outfit.name}»?
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
              <div className="flex items-center gap-4">
                {onEdit && (
                  <button
                    type="button"
                    onClick={() => onEdit(outfit)}
                    className="editorial-link text-stone-500"
                  >
                    Editar
                  </button>
                )}
                <button
                  type="button"
                  onClick={() => setIsConfirmingDelete(true)}
                  className="editorial-link text-stone-500 hover:text-oxblood"
                >
                  Eliminar
                </button>
              </div>
            )}
          </div>
        )}

        {actionError && (
          <p role="alert" className="font-serif text-xs text-red-700">
            {friendlyError(
              actionError,
              'No pudimos actualizar el outfit. Intentá de nuevo.',
            )}
          </p>
        )}
      </div>
    </article>
  )
}

/** Mirrors the card structure so the grid never jumps when data arrives. */
export function OutfitCardSkeleton() {
  return (
    <div aria-hidden="true" className="flex h-full flex-col bg-white">
      <div className="aspect-[2/3] animate-pulse bg-stone-200" />
      <div className="flex flex-1 flex-col gap-2 p-4">
        <div className="h-5 w-2/3 animate-pulse bg-stone-200" />
        <div className="h-3 w-1/2 animate-pulse bg-stone-100" />
        <div className="mt-2 h-3 w-1/4 animate-pulse bg-stone-100" />
      </div>
    </div>
  )
}
