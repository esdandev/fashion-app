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

function isSameSelection(
  left: readonly string[],
  right: readonly string[],
): boolean {
  if (left.length !== right.length) return false
  const ids = new Set(right)
  return left.every((id) => ids.has(id))
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

  // Archived garments are those from the original outfit that are no longer in
  // the active wardrobe. The section reflects the current selection: after
  // "Quitar" the row disappears and cannot be re-added, because the garment is
  // not offered anywhere else.
  const archivedSelection = useMemo(
    () =>
      initialSelection.filter(
        (garment) =>
          selectedIds.includes(garment.id) && !wardrobeIds.has(garment.id),
      ),
    [initialSelection, selectedIds, wardrobeIds],
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

  const initialIds = useMemo(
    () => [...new Set(initialSelection.map((garment) => garment.id))],
    [initialSelection],
  )
  const archivedIds = useMemo(
    () => new Set(archivedSelection.map((garment) => garment.id)),
    [archivedSelection],
  )
  const selectionChanged = !isSameSelection(selectedIds, initialIds)
  const keepsArchived = selectedIds.some((id) => archivedIds.has(id))
  // The editor drops archived ids whenever the selection changes, so the
  // validation must run against exactly that submittable selection. Otherwise
  // a "valid" state could rely on an archived shoe and fail on the server.
  const willDropArchived = keepsArchived && selectionChanged
  const submittableSelection = willDropArchived
    ? selectedGarments.filter((garment) => !archivedIds.has(garment.id))
    : selectedGarments

  const validation = validateOutfitGarments(submittableSelection)
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

  const previewItems: OutfitItem[] = submittableSelection.map(
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
      garmentIds: submittableSelection.map((garment) => garment.id),
    })
  }

  return (
    <div data-testid="outfit-builder" className="space-y-5">
      <form onSubmit={handleSubmit} className="space-y-5" noValidate>
        <div className="grid gap-4 sm:grid-cols-2">
          <label
            htmlFor={`${idPrefix}-name`}
            className="editorial-label flex flex-col gap-1"
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
              className="editorial-field"
            />
          </label>

          <label
            htmlFor={`${idPrefix}-occasion`}
            className="editorial-label flex flex-col gap-1"
          >
            Ocasión
            <input
              id={`${idPrefix}-occasion`}
              type="text"
              value={occasion}
              onChange={(event) => setOccasion(event.target.value)}
              maxLength={60}
              placeholder="Ej: trabajo, cena, casual"
              className="editorial-field"
            />
          </label>
        </div>

        <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
          <label
            htmlFor={`${idPrefix}-search`}
            className="editorial-label flex flex-1 flex-col gap-1"
          >
            Buscar prendas
            <input
              id={`${idPrefix}-search`}
              type="search"
              value={search}
              onChange={(event) => setSearch(event.target.value)}
              placeholder="Buscar por nombre"
              className="editorial-field"
            />
          </label>

          <label
            htmlFor={`${idPrefix}-category`}
            className="editorial-label flex flex-col gap-1"
          >
            Categoría
            <select
              id={`${idPrefix}-category`}
              value={categoryFilter}
              onChange={(event) =>
                setCategoryFilter(event.target.value as '' | GarmentCategory)
              }
              className="editorial-field sm:w-auto"
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
              <p className="border border-dashed border-stone-400 bg-stone-50 px-4 py-8 text-center font-serif text-sm text-stone-600">
                {wardrobe.length === 0
                  ? 'Tu guardarropa está vacío: subí prendas para armar un outfit.'
                  : 'No hay prendas con esa búsqueda.'}
              </p>
            )}

            {groups.map((group) => (
              <fieldset key={group.value}>
                <legend className="editorial-label mb-2 w-full border-b border-stone-300/80 pb-1">
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
                          className={`flex cursor-pointer items-center gap-3 border p-2.5 transition-colors ${
                            isSelected
                              ? 'border-oxblood bg-stone-100'
                              : 'border-stone-300/80 bg-white hover:border-stone-500'
                          }`}
                        >
                          <input
                            id={inputId}
                            type="checkbox"
                            checked={isSelected}
                            onChange={() => toggleGarment(garment.id)}
                            className="size-4 shrink-0 accent-oxblood"
                          />
                          <img
                            src={mediaUrl(garment.thumbUrl)}
                            alt=""
                            width={40}
                            height={40}
                            loading="lazy"
                            decoding="async"
                            className="size-10 shrink-0 border border-stone-300/80 object-cover"
                          />
                          <span className="min-w-0 flex-1">
                            <span className="block truncate font-display text-[17px] leading-tight text-ink">
                              {garment.name}
                            </span>
                            <span className="editorial-meta block truncate uppercase">
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
                className="border-l-2 border-gold bg-stone-50 p-3"
              >
                <h3 className="editorial-label">
                  Prendas archivadas del outfit original
                </h3>
                <ul className="mt-2 space-y-1.5">
                  {archivedSelection.map((garment) => (
                    <li
                      key={garment.id}
                      className="flex items-center justify-between gap-3 font-serif text-sm text-stone-700"
                    >
                      <span className="min-w-0">
                        <span className="block truncate">{garment.name}</span>
                        <span className="editorial-meta block uppercase">
                          Archivada · ya no está en el guardarropa
                        </span>
                      </span>
                      <button
                        type="button"
                        onClick={() => toggleGarment(garment.id)}
                        aria-label={`Quitar ${garment.name}`}
                        className="editorial-btn-outline shrink-0 px-2.5 py-1 text-[10px]"
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
            <h3 className="editorial-label border-b border-stone-300/80 pb-1">
              Vista previa
            </h3>
            <OutfitCollage
              items={previewItems}
              name={trimmedName || 'Vista previa del outfit'}
            />
            <p className="editorial-meta uppercase">
              {submittableSelection.length === 1
                ? '1 prenda seleccionada'
                : `${submittableSelection.length} prendas seleccionadas`}
            </p>
            {selectedIds.length > 0 && (
              <button
                type="button"
                onClick={clearSelection}
                className="editorial-link text-stone-500"
              >
                Limpiar selección
              </button>
            )}
          </aside>
        </div>

        <div className="space-y-2 border-t border-stone-300/80 pt-4">
          {willDropArchived && (
            <p
              role="note"
              className="border-l-2 border-gold bg-stone-50 px-3 py-2 font-serif text-sm text-stone-700"
            >
              Al guardar se quitarán las prendas archivadas: ya no están en el
              guardarropa.
            </p>
          )}
          {!validation.canSave && validation.message && (
            <p role="status" className="font-serif text-sm text-gold">
              {validation.message}
            </p>
          )}
          {nameError && (
            <p role="alert" className="font-serif text-sm text-red-700">
              {nameError}
            </p>
          )}
          {saveError && (
            <p role="alert" className="font-serif text-sm text-red-700">
              {saveError}
            </p>
          )}

          <div className="flex flex-wrap gap-2">
            <button
              type="submit"
              disabled={!canSubmit}
              className="editorial-btn"
            >
              {isSaving ? 'Guardando…' : 'Guardar outfit'}
            </button>
            {onCancel && (
              <button
                type="button"
                onClick={onCancel}
                disabled={isSaving}
                className="editorial-btn-outline"
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
