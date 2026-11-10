import { useEffect, useId, useRef, useState } from 'react'
import type { ChangeEvent } from 'react'
import { ApiError, api } from '../../lib/api'
import { GarmentForm } from './GarmentForm'
import {
  garmentValuesFromFile,
  validateGarmentFile,
  validateGarmentValues,
} from './garmentUpload'
import type { GarmentFormValues } from './garmentUpload'

interface Draft {
  key: string
  file: File
  previewUrl: string
  values: GarmentFormValues
  status: 'ready' | 'saving' | 'saved' | 'error'
  error?: string
}

interface UploadGarmentDialogProps {
  onClose: () => void
  onSaved: () => void
}

/**
 * Batch manual upload: select many photos, fill each garment's attributes and
 * save them all. No AI analysis happens here.
 */
export function UploadGarmentDialog({
  onClose,
  onSaved,
}: UploadGarmentDialogProps) {
  const titleId = useId()
  const inputRef = useRef<HTMLInputElement>(null)
  const [drafts, setDrafts] = useState<Draft[]>([])
  const [fileError, setFileError] = useState<string | null>(null)
  const [isSaving, setIsSaving] = useState(false)
  const draftsRef = useRef<Draft[]>([])

  draftsRef.current = drafts

  useEffect(() => {
    inputRef.current?.focus()
  }, [])

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape' && !isSaving) {
        close()
      }
    }

    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isSaving])

  function close() {
    for (const draft of draftsRef.current) {
      URL.revokeObjectURL(draft.previewUrl)
    }
    onClose()
  }

  function handleFiles(event: ChangeEvent<HTMLInputElement>) {
    const files = Array.from(event.target.files ?? [])
    const errors: string[] = []
    const next: Draft[] = []

    for (const file of files) {
      const error = validateGarmentFile(file)
      if (error) {
        errors.push(`${file.name}: ${error}`)
        continue
      }

      next.push({
        key: `${file.name}-${file.lastModified}-${Math.random().toString(36).slice(2, 8)}`,
        file,
        previewUrl: URL.createObjectURL(file),
        values: garmentValuesFromFile(file),
        status: 'ready',
      })
    }

    setDrafts((current) => [...current, ...next])
    setFileError(errors.length > 0 ? errors.join(' ') : null)
    event.target.value = ''
  }

  function updateDraft(key: string, values: GarmentFormValues) {
    setDrafts((current) =>
      current.map((draft) =>
        draft.key === key
          ? { ...draft, values, status: 'ready', error: undefined }
          : draft,
      ),
    )
  }

  function removeDraft(key: string) {
    setDrafts((current) => {
      const draft = current.find((item) => item.key === key)
      if (draft) {
        URL.revokeObjectURL(draft.previewUrl)
      }
      return current.filter((item) => item.key !== key)
    })
  }

  async function saveAll() {
    setIsSaving(true)
    let hasErrors = false

    for (const draft of draftsRef.current) {
      if (draft.status === 'saved') {
        continue
      }

      const validation = validateGarmentValues(draft.values)
      if (validation) {
        hasErrors = true
        setDrafts((current) =>
          current.map((item) =>
            item.key === draft.key
              ? { ...item, status: 'error', error: validation }
              : item,
          ),
        )
        continue
      }

      setDrafts((current) =>
        current.map((item) =>
          item.key === draft.key ? { ...item, status: 'saving' } : item,
        ),
      )

      try {
        await api.createGarment(draft.file, draft.values)
        setDrafts((current) =>
          current.map((item) =>
            item.key === draft.key ? { ...item, status: 'saved' } : item,
          ),
        )
      } catch (caught) {
        hasErrors = true
        const message =
          caught instanceof ApiError
            ? caught.message
            : 'No pudimos guardar la prenda. Intentá de nuevo.'
        setDrafts((current) =>
          current.map((item) =>
            item.key === draft.key
              ? { ...item, status: 'error', error: message }
              : item,
          ),
        )
      }
    }

    setIsSaving(false)

    if (!hasErrors) {
      onSaved()
    }
  }

  const pendingCount = drafts.filter((draft) => draft.status !== 'saved').length

  return (
    <div
      className="fixed inset-0 z-50 flex items-end justify-center bg-ink/50 p-0 backdrop-blur-sm sm:items-center sm:p-4"
      role="presentation"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget && !isSaving) {
          close()
        }
      }}
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        className="flex max-h-[95vh] w-full max-w-3xl flex-col overflow-hidden border border-ink bg-paper shadow-2xl"
      >
        <div className="flex items-start justify-between gap-4 border-b border-ink/60 p-5 sm:p-6">
          <div>
            <p className="editorial-kicker">Alta manual</p>
            <h2
              id={titleId}
              className="mt-2 font-display text-4xl leading-none tracking-[-0.045em] text-ink"
            >
              Subir prendas
            </h2>
            <p className="mt-2 font-serif text-sm text-stone-600">
              Elegí una o varias fotos y completá los datos de cada prenda.
            </p>
          </div>
          <button
            type="button"
            onClick={close}
            disabled={isSaving}
            className="border border-stone-400 px-2.5 py-1 font-sans text-lg leading-none text-stone-600 transition-colors hover:border-ink hover:text-ink disabled:cursor-not-allowed disabled:opacity-50"
            aria-label="Cerrar"
          >
            ×
          </button>
        </div>

        <div className="min-h-0 flex-1 overflow-y-auto p-5 sm:p-6">
          <label className="flex cursor-pointer flex-col items-center justify-center border border-dashed border-stone-400 bg-stone-50 px-4 py-8 text-center transition-colors hover:border-oxblood hover:bg-white">
            <span className="font-sans text-[11px] font-semibold tracking-[0.14em] text-ink uppercase">
              Seleccionar fotos
            </span>
            <span className="mt-1 font-serif text-xs text-stone-600">
              JPEG, PNG o WebP. Hasta 10 MB por imagen. Podés elegir varias.
            </span>
            <input
              ref={inputRef}
              type="file"
              accept="image/jpeg,image/png,image/webp"
              multiple
              onChange={handleFiles}
              disabled={isSaving}
              className="sr-only"
            />
          </label>

          {fileError && (
            <p
              role="alert"
              className="mt-3 border-l-2 border-gold bg-amber-50 p-3 font-serif text-sm text-amber-900"
            >
              {fileError}
            </p>
          )}

          {drafts.length === 0 ? (
            <p className="mt-6 text-center font-serif text-sm text-stone-500 italic">
              Todavía no elegiste fotos.
            </p>
          ) : (
            <ul className="mt-5 space-y-4">
              {drafts.map((draft) => (
                <li
                  key={draft.key}
                  className="border border-stone-300/80 bg-white p-4"
                >
                  <div className="flex flex-col gap-4 sm:flex-row">
                    <img
                      src={draft.previewUrl}
                      alt={`Vista previa de ${draft.file.name}`}
                      width={96}
                      height={96}
                      className="size-24 shrink-0 border border-stone-300/80 object-cover"
                    />
                    <div className="min-w-0 flex-1">
                      <GarmentForm
                        values={draft.values}
                        onChange={(values) => updateDraft(draft.key, values)}
                        disabled={isSaving || draft.status === 'saved'}
                        onRemove={() => removeDraft(draft.key)}
                      />
                    </div>
                  </div>

                  {draft.status === 'saved' && (
                    <p className="mt-3 font-sans text-xs font-medium tracking-[0.08em] text-sage uppercase">
                      Guardada.
                    </p>
                  )}
                  {draft.status === 'error' && draft.error && (
                    <p role="alert" className="mt-3 font-serif text-sm text-red-700">
                      {draft.error}
                    </p>
                  )}
                </li>
              ))}
            </ul>
          )}
        </div>

        <div className="flex flex-wrap items-center justify-between gap-3 border-t border-ink/60 p-5 sm:p-6">
          <p className="editorial-meta uppercase">
            {drafts.length === 0
              ? 'Sin fotos seleccionadas.'
              : `${drafts.length} ${drafts.length === 1 ? 'foto' : 'fotos'} · ${pendingCount} pendientes`}
          </p>
          <div className="flex gap-2">
            <button
              type="button"
              onClick={close}
              disabled={isSaving}
              className="editorial-btn-outline"
            >
              Cerrar
            </button>
            <button
              type="button"
              onClick={() => void saveAll()}
              disabled={isSaving || pendingCount === 0}
              className="editorial-btn"
            >
              {isSaving ? 'Guardando…' : 'Guardar todas'}
            </button>
          </div>
        </div>
      </div>
    </div>
  )
}
