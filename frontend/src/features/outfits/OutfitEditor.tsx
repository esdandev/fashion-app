import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError, api } from '../../lib/api'
import type { Outfit } from '../../lib/types'
import { OutfitBuilder } from './OutfitBuilder'
import type { OutfitBuilderDraft } from './OutfitBuilder'

interface OutfitEditorProps {
  /** Existing outfit to edit, or `undefined` to create a new one. */
  outfit?: Outfit
  /** Called after a successful save (the page goes back to the list). */
  onClose: () => void
}

/**
 * Data layer around `OutfitBuilder`: loads the active wardrobe across pages,
 * prefills the builder when editing, and persists with POST/PATCH. The builder
 * itself stays presentational and testable with a wardrobe fixture.
 */
export function OutfitEditor({ outfit, onClose }: OutfitEditorProps) {
  const queryClient = useQueryClient()

  const wardrobeQuery = useQuery({
    queryKey: ['garments', 'outfit-builder'],
    queryFn: ({ signal }) => api.getAllGarments(5, signal),
    staleTime: 60 * 1000,
  })

  const saveMutation = useMutation({
    mutationFn: (draft: OutfitBuilderDraft) => {
      const occasion = draft.occasion

      if (!outfit) {
        return api.createOutfit({
          name: draft.name,
          occasion,
          garmentIds: draft.garmentIds,
        })
      }

      const originalIds = outfit.items.map((item) => item.garment.id)
      const isSameSelection =
        draft.garmentIds.length === originalIds.length &&
        originalIds.every((id) => draft.garmentIds.includes(id))

      // Keeping the composition untouched must not resend archived ids: the
      // API answers 409 for them. Renaming or changing the occasion is enough.
      if (isSameSelection) {
        return api.patchOutfit(outfit.id, {
          name: draft.name,
          // An empty string clears the column; `null` means "not provided".
          occasion: occasion ?? '',
        })
      }

      // Once the selection changes, only active garments can be part of the
      // replacement. Archived items kept from the old outfit are dropped here.
      const activeIds = new Set(
        (wardrobeQuery.data ?? []).map((garment) => garment.id),
      )
      return api.patchOutfit(outfit.id, {
        name: draft.name,
        occasion: occasion ?? '',
        garmentIds: draft.garmentIds.filter((id) => activeIds.has(id)),
      })
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['outfits'] })
      onClose()
    },
  })

  const initialSelection = outfit
    ? [...outfit.items]
        .sort((left, right) => left.sortOrder - right.sortOrder)
        .map((item) => item.garment)
    : []

  const saveError = saveMutation.error
    ? saveMutation.error instanceof ApiError
      ? saveMutation.error.message
      : 'No pudimos guardar el outfit. Revisá tu conexión e intentá de nuevo.'
    : null

  return (
    <section
      aria-labelledby="outfit-editor-title"
      className="border border-stone-300/80 bg-white p-4 sm:p-6"
    >
      <div className="mb-5 border-b border-stone-300/80 pb-4">
        <p className="editorial-kicker">
          {outfit ? 'Editar composición' : 'Nueva composición'}
        </p>
        <h2
          id="outfit-editor-title"
          className="mt-2 font-display text-3xl leading-none tracking-[-0.04em] text-ink"
        >
          {outfit ? 'Editar outfit' : 'Nuevo outfit'}
        </h2>
        <p className="mt-2 max-w-2xl font-serif text-sm leading-relaxed text-stone-600">
          {outfit
            ? 'Cambiá el nombre, la ocasión o las prendas. Al guardar volvés al listado.'
            : 'Elegí las prendas y mirá el collage mientras lo armás. Al guardar volvés al listado.'}
        </p>
      </div>

      {wardrobeQuery.isPending && (
        <p
          role="status"
          className="border border-stone-300/80 bg-stone-50 p-6 text-center font-serif text-sm text-stone-600"
        >
          Cargando tu guardarropa…
        </p>
      )}

      {wardrobeQuery.isError && (
        <div
          role="alert"
          className="border border-red-200 bg-red-50 p-4 font-sans text-sm text-red-900"
        >
          <p className="font-medium">No pudimos cargar el guardarropa.</p>
          <p className="mt-1 text-red-800">
            {wardrobeQuery.error instanceof ApiError
              ? wardrobeQuery.error.message
              : 'Revisá tu conexión e intentá de nuevo.'}
          </p>
          <button
            type="button"
            onClick={() => void wardrobeQuery.refetch()}
            className="editorial-btn-outline mt-3 border-red-300 text-red-900 hover:bg-red-900"
          >
            Reintentar
          </button>
        </div>
      )}

      {wardrobeQuery.data && !wardrobeQuery.isError && (
        <OutfitBuilder
          key={outfit?.id ?? 'nuevo'}
          wardrobe={wardrobeQuery.data}
          initialSelection={initialSelection}
          initialName={outfit?.name ?? ''}
          initialOccasion={outfit?.occasion ?? ''}
          onSave={(draft) => saveMutation.mutate(draft)}
          onCancel={onClose}
          isSaving={saveMutation.isPending}
          saveError={saveError}
        />
      )}
    </section>
  )
}
