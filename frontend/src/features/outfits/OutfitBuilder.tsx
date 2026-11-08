import { useId, useMemo, useState } from 'react'
import type { FormEvent } from 'react'
import { mediaUrl } from '../../lib/api'
import type { Garment, GarmentCategory, OutfitItem } from '../../lib/types'
import { CATEGORY_OPTIONS, categoryLabel } from '../wardrobe/garmentLabels'
import { OutfitCollage } from './OutfitCollage'
import { validateOutfitGarments } from './outfitRules'

export interface OutfitBuilderDraft {
  name: string
  occasion: string | null
  garmentIds: string[]
}

interface OutfitBuilderProps {
  /** Active wardrobe, already loaded by the caller. */
  wardrobe: Garment[]
  /** Garments of the outfit being edited; may include archived ones. */
  initialSelection?: Garment[]
  initialName?: string
  initialOccasion?: string | null
  onSave: (draft: OutfitBuilderDraft) => void
  onCancel?: () => void
  isSaving?: boolean
  saveError?: string | null
}

/** Mirrors the API `LayerFor`: base pieces, then outerwear, then accessories. */
function layerFor(category: GarmentCategory): number {
  if (category === 'outerwear') return 1
  if (category === 'bag' || category === 'accessory') return 2
  return 0
}

/** Case/accent-insensitive search so "bota" matches "Botas" and "botín". */
function normalize(value: string): string {
  return value
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLocaleLowerCase('es')
}

export function OutfitBuilder({
  wardrobe,
  initialSelection = [],
  initialName = '',
  initialOccasion = '',
  onSave,
  onCancel,
  isSaving = false,
  saveError = null,
}: OutfitBuilderProps) {
  const idPrefix = useId()
  const [name, setName] = useState(initialName)
  const [occasion, setOccasion] = useState(initialOccasion ?? '')
  const [selectedIds, setSelectedIds] = useState<string[]>(() =>
    [...new Set(initialSelection.map((garment) => garment.id))],
  )
  const [search, setSearch] = useState('')
  const [categoryFilter, setCategoryFilter] = useState<'' | GarmentCategory>('')
  const [nameError, setNameError] = useState<string | null>(null)

  const wardrobeIds = useMemo(
    () => new Set(wardrobe.map((garment) => garment.id)),
    [wardrobe],
  )

  // Initial garments that are not in the active wardrobe are archived: they
  // stay visible (and selected) so an existing outfit can be edited without
  // losing them, but they are never offered again.
  const archivedSelection = useMemo(
    () =>
      initialSelection.filter(
        (garment) => !wardrobeIds.has(garment.id),
      ),
    [initialSelection, wardrobeIds],
  )

  const garmentsById = useMemo(() => {
    const map = new Map<string, Garment>()
    for (const garment of [...archivedSelection, ...wardrobe]) {
      map.set(garment.id, garment)
    }
    return map
  }, [archivedSelection, wardrobe])

  const selectedGarments = useMemo(
    () =>
      selectedIds
        .map((id) => garmentsById.get(id))
        .filter((garment): garment is Garment => garment !== undefined),
    [selectedIds, garmentsById],
  )

  const validation = validateOutfitGarments(selectedGarments)
  const trimmedName = name.trim()
  const canSubmit = validation.canSave && !isSaving

  const normalizedSearch = normalize(search.trim())
  const filteredWardrobe = wardrobe.filter((garment) => {
    if (categoryFilter && garment.category !== categoryFilter) return false
    if (!normalizedSearch) return true
    return normalize(garment.name).includes(normalizedSearch)
  })

  const groups = CATEGORY_OPTIONS.map((option) => ({
    ...option,
    garments: filteredWardrobe
      .filter((garment) => garment.category === option.value)
      .sort((left, right) => left.name.localeCompare(right.name, 'es')),
  })).filter((group) => group.garments.length > 0)

  const previewItems: OutfitItem[] = selectedGarments.map(
    (garment, index) => ({
      garment,
      layer: layerFor(garment.category),
      sortOrder: index,
    }),
  )

  function toggleGarment(id: string) {
    setSelectedIds((current) =>
      current.includes(id)
        ? current.filter((candidate) => candidate !== id)
        : [...current, id],
    )
  }

  function clearSelection() {
    setSelectedIds([])
  }

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!validation.canSave) return

    if (!trimmedName) {
      setNameError('Poné un nombre para guardar el outfit.')
      return
    }

    setNameError(null)
    onSave({
      name: trimmedName,
      occasion: occasion.trim() ? occasion.trim() : null,
      garmentIds: selectedIds,
    })
  }

  return (
    <div data-testid="outfit-builder" className="space-y-5">
      <form onSubmit={handleSubmit} className="space-y-5" noValidate>
        <div className="grid gap-4 sm:grid-cols-2">
          <label
            htmlFor={`${idPrefix}-name`}
            className="flex flex-col gap-1 text-xs font-medium text-stone-500"
          >
            Nombre
            <input
              id={`${idPrefix}-name`}
              type="text"
              value={name}
              onChange={(event) => {
                setName(event.target.value)
                if (nameError) setNameError(null)
              }}
              maxLength={120}
              placeholder="Ej: Look de trabajo"
              className="rounded-xl border border-stone-300 bg-white px-3 py-2 text-sm text-stone-800 shadow-sm transition-colors focus:border-stone-500 focus:ring-2 focus:ring-stone-300 focus:outline-none"
            />
          </label>

          <label
            htmlFor={`${idPrefix}-occasion`}
            className="flex flex-col gap-1 text-xs font-medium text-stone-500"
          >
            Ocasión
            <input
              id={`${idPrefix}-occasion`}
              type="text"
              value={occasion}
              onChange={(event) => setOccasion(event.target.value)}
              maxLength={60}
              placeholder="Ej: trabajo, cena, casual"
              className="rounded-xl border border-stone-300 bg-white px-3 py-2 text-sm text-stone-800 shadow-sm transition-colors focus:border-stone-500 focus:ring-2 focus:ring-stone-300 focus:outline-none"
            />
          </label>
        </div>

        <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
          <label
            htmlFor={`${idPrefix}-search`}
            className="flex flex-1 flex-col gap-1 text-xs font-medium text-stone-500"
          >
            Buscar prendas
            <input
              id={`${idPrefix}-search`}
              type="search"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              placeholder="Buscar por nombre"
              className="rounded-xl border border-stone-300 bg-white px-3 py-2 text-sm text-stone-800 shadow-sm transition-colors focus:border-stone-500 focus:ring-2 focus:ring-stone-300 focus:outline-none"
            />
          </label>

          <label
            htmlFor={`${idPrefix}-category`}
            className="flex flex-col gap-1 text-xs font-medium text-stone-500"
          >
            Categoría
            <select
              id={`${idPrefix}-category`}
              value={categoryFilter}
              onChange={(event) =>
                setCategoryFilter(event.target.value as '' | GarmentCategory)
              }
              className="rounded-xl border border-stone-300 bg-white px-3 py-2 text-sm text-stone-800 shadow-sm transition-colors focus:border-stone-500 focus:ring-2 focus:ring-stone-300 focus:outline-none sm:w-auto"
            >
              <option value="">Todas</option>
              {CATEGORY_OPTIONS.map((option) => (
                <option key={option.value} value={option.value}>
                  {option.label}
                </option>
              ))}
            </select>
          </label>
        </div>

        <div className="grid gap-5 lg:grid-cols-[minmax(0,1fr)_18rem]">
          <div className="space-y-4">
            {groups.length === 0 && (
              <p className="rounded-xl border border-dashed border-stone-300 bg-stone-50 px-4 py-8 text-center text-sm text-stone-500">
                {wardrobe.length === 0
                  ? 'Tu guardarropa está vacío: subí prendas para armar un outfit.'
                  : 'No hay prendas con esa búsqueda.'}
              </p>
            )}

            {groups.map((group) => (
              <fieldset key={group.value}>
                <legend className="mb-2 text-xs font-semibold tracking-wide text-stone-500 uppercase">
                  {group.label}
                </legend>
                <ul className="grid gap-2 sm:grid-cols-2">
                  {group.garments.map((garment) => {
                    const inputId = `${idPrefix}-${garment.id}`
                    const isSelected = selectedIds.includes(garment.id)

                    return (
                      <li key={garment.id}>
                        <label
                          htmlFor={inputId}
                          className={`flex cursor-pointer items-center gap-3 rounded-xl border p-2.5 transition-colors ${
                            isSelected
                              ? 'border-stone-900 bg-stone-50'
                              : 'border-stone-200 bg-white hover:border-stone-400'
                          }`}
                        >
                          <input
                            id={inputId}
                            type="checkbox"
                            checked={isSelected}
                            onChange={() => toggleGarment(garment.id)}
                            className="size-4 shrink-0 accent-stone-900"
                          />
                          <img
                            src={mediaUrl(garment.thumbUrl)}
                            alt=""
                            width={40}
                            height={40}
                            loading="lazy"
                            decoding="async"
                            className="size-10 shrink-0 rounded-lg object-cover"
                          />
                          <span className="min-w-0 flex-1">
                            <span className="block truncate text-sm text-stone-900">
                              {garment.name}
                            </span>
                            <span className="block truncate text-[11px] text-stone-500">
                              {categoryLabel(garment.category)} · Formalidad{' '}
                              {garment.formality}
                            </span>
                          </span>
                        </label>
                      </li>
                    )
                  })}
                </ul>
              </fieldset>
            ))}

            {archivedSelection.length > 0 && (
              <section
                aria-label="Prendas archivadas"
                className="rounded-xl border border-amber-200 bg-amber-50 p-3"
              >
                <h3 className="text-xs font-semibold tracking-wide text-amber-900 uppercase">
                  Prendas archivadas del outfit original
                </h3>
                <ul className="mt-2 space-y-1.5">
                  {archivedSelection.map((garment) => (
                    <li
                      key={garment.id}
                      className="flex items-center justify-between gap-3 text-sm text-amber-900"
                    >
                      <span className="min-w-0">
                        <span className="block truncate">{garment.name}</span>
                        <span className="block text-[11px] text-amber-800">
                          Archivada · ya no está en el guardarropa
                        </span>
                      </span>
                      <button
                        type="button"
                        onClick={() => toggleGarment(garment.id)}
                        className="shrink-0 rounded-lg border border-amber-300 bg-white px-2.5 py-1 text-xs font-medium text-amber-900 transition-colors hover:bg-amber-100"
                      >
                        Quitar
                      </button>
                    </li>
                  ))}
                </ul>
              </section>
            )}
          </div>

          <aside className="space-y-3">
            <h3 className="text-xs font-semibold tracking-wide text-stone-500 uppercase">
              Vista previa
            </h3>
            <OutfitCollage
              items={previewItems}
              name={trimmedName || 'Vista previa del outfit'}
            />
            <p className="text-xs text-stone-500">
              {selectedIds.length === 1
                ? '1 prenda seleccionada'
                : `${selectedIds.length} prendas seleccionadas`}
            </p>
            {selectedIds.length > 0 && (
              <button
                type="button"
                onClick={clearSelection}
                className="text-xs font-medium text-stone-500 underline-offset-4 transition-colors hover:text-stone-900 hover:underline"
              >
                Limpiar selección
              </button>
            )}
          </aside>
        </div>

        <div className="space-y-2 border-t border-stone-200 pt-4">
          {!validation.canSave && validation.message && (
            <p role="status" className="text-sm text-amber-800">
              {validation.message}
            </p>
          )}
          {nameError && (
            <p role="alert" className="text-sm text-red-700">
              {nameError}
            </p>
          )}
          {saveError && (
            <p role="alert" className="text-sm text-red-700">
              {saveError}
            </p>
          )}

          <div className="flex flex-wrap gap-2">
            <button
              type="submit"
              disabled={!canSubmit}
              className="rounded-xl bg-stone-900 px-4 py-2 text-sm font-medium text-white shadow-sm transition-colors hover:bg-stone-700 focus-visible:ring-2 focus-visible:ring-stone-400 focus-visible:ring-offset-2 focus-visible:outline-none disabled:cursor-not-allowed disabled:opacity-50"
            >
              {isSaving ? 'Guardando…' : 'Guardar outfit'}
            </button>
            {onCancel && (
              <button
                type="button"
                onClick={onCancel}
                disabled={isSaving}
                className="rounded-xl border border-stone-300 bg-white px-4 py-2 text-sm font-medium text-stone-700 transition-colors hover:bg-stone-100 disabled:cursor-not-allowed disabled:opacity-60"
              >
                Cancelar
              </button>
            )}
          </div>
        </div>
      </form>
    </div>
  )
}
