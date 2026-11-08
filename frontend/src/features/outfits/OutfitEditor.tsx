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
      className="rounded-2xl border border-stone-200 bg-white p-4 shadow-sm sm:p-6"
    >
      <div className="mb-4">
        <h2
          id="outfit-editor-title"
          className="font-display text-xl font-semibold tracking-tight text-stone-900"
        >
          {outfit ? 'Editar outfit' : 'Nuevo outfit'}
        </h2>
        <p className="mt-1 text-sm text-stone-500">
          {outfit
            ? 'Cambiá el nombre, la ocasión o las prendas. Al guardar volvés al listado.'
            : 'Elegí las prendas y mirá el collage mientras lo armás. Al guardar volvés al listado.'}
        </p>
      </div>

      {wardrobeQuery.isPending && (
        <p
          role="status"
          className="rounded-xl border border-stone-200 bg-stone-50 p-6 text-center text-sm text-stone-500"
        >
          Cargando tu guardarropa…
        </p>
      )}

      {wardrobeQuery.isError && (
        <div
          role="alert"
          className="rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-900"
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
            className="mt-3 rounded-xl border border-red-300 bg-white px-3 py-1.5 font-medium text-red-900 transition-colors hover:bg-red-100"
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
