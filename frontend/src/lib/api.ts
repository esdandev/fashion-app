import type {
  CreateGarmentResponse,
  Garment,
  GarmentAnalysisProposal,
  GarmentListResponse,
  PatchGarmentRequest,
  ProblemDetails,
} from './types'

const API_BASE_URL = (
  import.meta.env.VITE_API_URL ?? 'http://localhost:5210'
).replace(/\/+$/, '')

const TOKEN_STORAGE_KEY = 'app_token'

export class ApiError extends Error {
  readonly status: number
  readonly problemDetails?: ProblemDetails

  constructor(message: string, status: number, problemDetails?: ProblemDetails) {
    super(message)
    this.name = 'ApiError'
    this.status = status
    this.problemDetails = problemDetails
  }
}

/** Turns a relative `/media/...` path into an absolute URL on the API host. */
export function mediaUrl(path: string): string {
  if (/^https?:\/\//i.test(path)) {
    return path
  }

  return `${API_BASE_URL}${path.startsWith('/') ? path : `/${path}`}`
}

/**
 * The personal instance requires `X-App-Token`. The token is optional so the
 * public demo and local development keep working without it.
 */
function authHeaders(): Record<string, string> {
  try {
    const token = sessionStorage.getItem(TOKEN_STORAGE_KEY)
    return token ? { 'X-App-Token': token } : {}
  } catch {
    // sessionStorage can be unavailable (private mode / non-browser contexts).
    return {}
  }
}

async function readProblemDetails(
  response: Response,
): Promise<ProblemDetails | undefined> {
  try {
    return (await response.json()) as ProblemDetails
  } catch {
    return undefined
  }
}

async function parseResponse<T>(response: Response): Promise<T> {
  if (response.ok) {
    return (await response.json()) as T
  }

  const problem = await readProblemDetails(response)
  const message =
    problem?.detail ??
    problem?.title ??
    fallbackMessage(response.status)

  throw new ApiError(message, response.status, problem)
}

function fallbackMessage(status: number): string {
  if (status === 401) {
    return 'Falta el token de acceso. Guardalo en sessionStorage con la clave app_token.'
  }

  if (status === 403) {
    return 'Esta instancia es de solo lectura.'
  }

  return `No se pudo completar la solicitud (${status}).`
}

export const api = {
  async getGarments(
    query = '',
    signal?: AbortSignal,
  ): Promise<GarmentListResponse> {
    const response = await fetch(`${API_BASE_URL}/api/garments${query}`, {
      headers: authHeaders(),
      signal,
    })

    return parseResponse<GarmentListResponse>(response)
  },

  async postGarment(file: File): Promise<CreateGarmentResponse> {
    const body = new FormData()
    body.append('file', file)

    // No Content-Type: the browser adds the multipart boundary.
    const response = await fetch(`${API_BASE_URL}/api/garments`, {
      method: 'POST',
      headers: authHeaders(),
      body,
    })

    return parseResponse<CreateGarmentResponse>(response)
  },

  async reanalyze(id: string): Promise<GarmentAnalysisProposal> {
    const response = await fetch(
      `${API_BASE_URL}/api/garments/${encodeURIComponent(id)}/reanalyze`,
      {
        method: 'POST',
        headers: authHeaders(),
      },
    )

    return parseResponse<GarmentAnalysisProposal>(response)
  },

  /**
   * Persists the attributes the user confirmed. `POST /api/garments` creates the
   * garment from the photo (and runs the analysis); this call applies the edits
   * made in the confirmation form.
   */
  async patchGarment(
    id: string,
    patch: PatchGarmentRequest,
  ): Promise<Garment> {
    const response = await fetch(
      `${API_BASE_URL}/api/garments/${encodeURIComponent(id)}`,
      {
        method: 'PATCH',
        headers: {
          ...authHeaders(),
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(patch),
      },
    )

    return parseResponse<Garment>(response)
  },
}

export { API_BASE_URL }
