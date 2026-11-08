import { useEffect, useId, useRef, useState } from 'react'
import type { ChangeEvent, MouseEvent } from 'react'
import { ApiError, api } from '../../lib/api'
import type { GarmentAnalysisProposal } from '../../lib/types'
import { GarmentForm } from './GarmentForm'
import {
  EMPTY_GARMENT_FORM_VALUES,
  garmentFormValuesFromCurrentGarment,
  garmentFormValuesFromProposal,
  toConfirmRequest,
  toPatchRequest,
  validateGarmentFile,
} from './garmentUpload'
import type { GarmentFormValues } from './garmentUpload'

type Phase =
  | 'idle'
  | 'analyzing'
  | 'form'
  | 'saving'
  | 'reanalyzing'
  | 'upload-error'

interface UploadGarmentDialogProps {
  onClose: () => void
  onSaved: () => void
}

/**
 * Mounted only while the dialog is open, so every upload starts from a clean
 * state and in-flight requests never leak into the next session.
 */
export function UploadGarmentDialog({
  onClose,
  onSaved,
}: UploadGarmentDialogProps) {
  const titleId = useId()
  const inputRef = useRef<HTMLInputElement>(null)
  const requestRef = useRef(0)

  const [phase, setPhase] = useState<Phase>('idle')
  const [file, setFile] = useState<File | null>(null)
  const [previewUrl, setPreviewUrl] = useState<string | null>(null)
  const [contentHash, setContentHash] = useState<string | null>(null)
  const [analysis, setAnalysis] = useState<GarmentAnalysisProposal | null>(null)
  const [existingGarmentId, setExistingGarmentId] = useState<string | null>(
    null,
  )
  const [values, setValues] = useState<GarmentFormValues | null>(null)
  const [confidence, setConfidence] = useState<number | undefined>(undefined)
  const [deduplicated, setDeduplicated] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const busy =
    phase === 'analyzing' || phase === 'saving' || phase === 'reanalyzing'

  useEffect(() => {
    inputRef.current?.focus()
  }, [])

  // Object URLs live only as long as the preview they belong to.
  useEffect(() => {
    return () => {
      if (previewUrl) {
        URL.revokeObjectURL(previewUrl)
      }
    }
  }, [previewUrl])

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape' && !busy) {
        onClose()
      }
    }

    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [busy, onClose])

  async function analyze(selected: File) {
    const requestId = ++requestRef.current
    setPhase('analyzing')
    setError(null)
    setDeduplicated(false)

    try {
      const response = await api.analyzeGarment(selected)
      if (requestId !== requestRef.current) {
        return
      }

      setContentHash(response.contentHash)
      setAnalysis(response.analysis)
      setExistingGarmentId(response.existingGarmentId)
      setValues(
        response.currentGarment
          ? garmentFormValuesFromCurrentGarment(response.currentGarment)
          : garmentFormValuesFromProposal(
              response.analysis,
              EMPTY_GARMENT_FORM_VALUES,
            ),
      )
      setConfidence(response.analysis.confidence)
      setDeduplicated(response.existingGarmentId !== null)
      setPhase('form')
    } catch (caught) {
      if (requestId !== requestRef.current) {
        return
      }

      setError(
        friendlyError(
          caught,
          'No pudimos analizar la prenda. Revisá tu conexión e intentá de nuevo.',
        ),
      )
      setPhase('upload-error')
    }
  }

  async function handleFileChange(event: ChangeEvent<HTMLInputElement>) {
    const selected = event.target.files?.[0]
    // Reset the input so choosing the same file again still fires `change`.
    event.target.value = ''
    if (!selected) {
      return
    }

    setContentHash(null)
    setAnalysis(null)
    setExistingGarmentId(null)
    setValues(null)
    setDeduplicated(false)
    setError(null)

    const validationMessage = validateGarmentFile(selected)
    if (validationMessage) {
      setFile(null)
      setPreviewUrl(null)
      setPhase('idle')
      setError(validationMessage)
      return
    }

    setFile(selected)
    setPreviewUrl(URL.createObjectURL(selected))
    await analyze(selected)
  }

  async function handleSave() {
    if (!values || !contentHash) {
      return
    }

    if (values.name.trim().length === 0) {
      setError('El nombre no puede estar vacío.')
      return
    }

    const requestId = ++requestRef.current
    setPhase('saving')
    setError(null)

    try {
      if (existingGarmentId) {
        // The photo is already in the wardrobe: keep edits working through the
        // correction endpoint instead of creating (or silently deduplicating).
        await api.patchGarment(existingGarmentId, toPatchRequest(values))
      } else {
        await api.confirmGarment(
          toConfirmRequest(values, contentHash, {
            confidence,
            subcategory: analysis?.subcategory,
            pattern: analysis?.pattern,
            material: analysis?.material,
          }),
        )
      }

      if (requestId !== requestRef.current) {
        return
      }

      onSaved()
    } catch (caught) {
      if (requestId !== requestRef.current) {
        return
      }

      setError(
        friendlyError(
          caught,
          'No pudimos guardar los cambios. Revisá tu conexión e intentá de nuevo.',
        ),
      )
      setPhase('form')
    }
  }

  async function handleRetry() {
    if (busy) {
      return
    }

    if (existingGarmentId) {
      const requestId = ++requestRef.current
      setPhase('reanalyzing')
      setError(null)

      try {
        const proposal: GarmentAnalysisProposal =
          await api.reanalyze(existingGarmentId)
        if (requestId !== requestRef.current) {
          return
        }

        setAnalysis(proposal)
        setValues((current) =>
          garmentFormValuesFromProposal(
            proposal,
            current ?? EMPTY_GARMENT_FORM_VALUES,
          ),
        )
        setConfidence(proposal.confidence)
        // Deliberately no PATCH here: the proposal only refills the form.
        setPhase('form')
      } catch (caught) {
        if (requestId !== requestRef.current) {
          return
        }

        setError(
          friendlyError(
            caught,
            'No pudimos volver a analizar la prenda. Intentá de nuevo.',
          ),
        )
        setPhase('form')
      }
      return
    }

    // Draft: analyze the same file again. Nothing is persisted until Guardar.
    if (file) {
      await analyze(file)
    }
  }

  function handleClose() {
    if (busy) {
      return
    }

    onClose()
  }

  function handleBackdropClick(event: MouseEvent<HTMLDivElement>) {
    if (event.target === event.currentTarget) {
      handleClose()
    }
  }

  const showFileInput =
    phase === 'idle' || phase === 'analyzing' || phase === 'upload-error'
  const showForm =
    values !== null &&
    (phase === 'form' || phase === 'saving' || phase === 'reanalyzing')

  return (
    <div
      role="presentation"
      onMouseDown={handleBackdropClick}
      className="fixed inset-0 z-50 flex items-end justify-center bg-stone-950/40 backdrop-blur-sm sm:items-center sm:p-4"
    >
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        className="flex max-h-[92svh] w-full max-w-2xl flex-col overflow-y-auto rounded-t-3xl border border-stone-200 bg-white p-5 shadow-2xl sm:rounded-3xl sm:p-6"
      >
        <div className="flex items-start justify-between gap-4">
          <div>
            <h2
              id={titleId}
              className="font-display text-xl font-semibold tracking-tight text-stone-900"
            >
              Agregar prenda
            </h2>
            <p className="mt-0.5 text-sm text-stone-500">
              Subí una foto y confirmá lo que detectó la IA.
            </p>
          </div>
          <button
            type="button"
            onClick={handleClose}
            disabled={busy}
            aria-label="Cerrar"
            className="rounded-full p-2 text-stone-500 transition-colors hover:bg-stone-100 hover:text-stone-900 disabled:cursor-not-allowed disabled:opacity-50"
          >
            <svg
              aria-hidden="true"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              strokeLinecap="round"
              className="size-5"
            >
              <path d="M6 6l12 12M18 6L6 18" />
            </svg>
          </button>
        </div>

        {previewUrl && (
          <div className="mt-4 overflow-hidden rounded-2xl border border-stone-200 bg-stone-100">
            <img
              src={previewUrl}
              alt="Vista previa de la prenda"
              className="mx-auto max-h-64 w-auto object-contain"
            />
          </div>
        )}

        {showFileInput && (
          <div className="mt-4">
            <label
              htmlFor="garment-photo"
              className="flex flex-col gap-2 text-xs font-medium text-stone-500"
            >
              Foto de la prenda
              <input
                ref={inputRef}
                id="garment-photo"
                type="file"
                accept="image/jpeg,image/png,image/webp"
                onChange={handleFileChange}
                disabled={phase === 'analyzing'}
                className="w-full cursor-pointer rounded-xl border border-dashed border-stone-300 bg-stone-50 px-3 py-2 text-sm text-stone-700 file:mr-3 file:cursor-pointer file:rounded-lg file:border-0 file:bg-stone-900 file:px-3 file:py-1.5 file:text-xs file:font-medium file:text-white hover:border-stone-400 disabled:cursor-not-allowed disabled:opacity-60"
              />
            </label>
            <p className="mt-1.5 text-[11px] text-stone-400">
              JPEG, PNG o WebP · máximo 10 MB.
            </p>
          </div>
        )}

        {phase === 'idle' && !error && (
          <p className="mt-4 rounded-xl bg-stone-50 p-3 text-xs text-stone-500">
            Elegí una foto y la IA va a proponer nombre, categoría, color,
            temporada, formalidad y etiquetas.
          </p>
        )}

        {error && phase === 'idle' && (
          <div
            role="alert"
            className="mt-4 rounded-xl border border-red-200 bg-red-50 p-3 text-sm text-red-900"
          >
            {error}
          </div>
        )}

        {phase === 'analyzing' && (
          <div
            role="status"
            className="mt-4 flex items-center gap-3 rounded-xl border border-stone-200 bg-stone-50 p-4 text-sm text-stone-700"
          >
            <span
              aria-hidden="true"
              className="size-5 animate-spin rounded-full border-2 border-stone-300 border-t-stone-900"
            />
            Analizando prenda…
          </div>
        )}

        {phase === 'upload-error' && error && (
          <div
            role="alert"
            className="mt-4 rounded-xl border border-red-200 bg-red-50 p-4 text-sm text-red-900"
          >
            <p>{error}</p>
            <button
              type="button"
              onClick={() => void handleRetry()}
              disabled={!file}
              className="mt-3 rounded-xl border border-red-300 bg-white px-3 py-1.5 font-medium text-red-900 transition-colors hover:bg-red-100 disabled:cursor-not-allowed disabled:opacity-60"
            >
              Reintentar
            </button>
          </div>
        )}

        {deduplicated && showForm && (
          <p className="mt-4 rounded-xl bg-amber-50 p-3 text-xs text-amber-900">
            Esta foto ya estaba en tu guardarropa. Podés ajustar los datos y
            guardar los cambios.
          </p>
        )}

        {showForm && values && (
          <GarmentForm
            values={values}
            onChange={setValues}
            onSave={() => void handleSave()}
            onRetry={() => void handleRetry()}
            isSaving={phase === 'saving'}
            isReanalyzing={phase === 'reanalyzing'}
            confidence={confidence}
            error={error}
          />
        )}
      </div>
    </div>
  )
}

function friendlyError(caught: unknown, fallback: string): string {
  if (caught instanceof ApiError) {
    if (caught.status === 413) {
      return 'La imagen no puede superar los 10 MB.'
    }

    if (caught.status === 415) {
      return 'Solo se aceptan imágenes JPEG, PNG o WebP.'
    }

    return caught.message
  }

  return fallback
}
