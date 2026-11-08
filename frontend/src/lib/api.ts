import type {
  AnalyzeGarmentResponse,
  AppConfig,
  ConfirmGarmentRequest,
  CreateGarmentResponse,
  CreateOutfitRequest,
  Garment,
  GarmentAnalysisProposal,
  GarmentListResponse,
  Outfit,
  PatchGarmentRequest,
  PatchOutfitRequest,
  ProblemDetails,
  SuggestOutfitsRequest,
  SuggestOutfitsResponse,
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

  /**
   * The active wardrobe needed by the outfit builder. It walks the cursor
   * pages but stops at `maxPages` (5 × 24 = 120 garments by default) so a very
   * large closet never turns the builder into an unbounded request chain.
   */
  async getAllGarments(
    maxPages = 5,
    signal?: AbortSignal,
  ): Promise<Garment[]> {
    const garments: Garment[] = []
    let cursor: string | null = null

    for (let page = 0; page < maxPages; page += 1) {
      const query = cursor ? `?cursor=${encodeURIComponent(cursor)}` : ''
      const { items, nextCursor } = await this.getGarments(query, signal)
      garments.push(...items)

      if (!nextCursor) break
      cursor = nextCursor
    }

    return garments
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

  /**
   * Normalizes, stores and analyzes a photo without creating a garment. The
   * returned content hash is later sent to `confirmGarment` when the user saves.
   */
  async analyzeGarment(file: File): Promise<AnalyzeGarmentResponse> {
    const body = new FormData()
    body.append('file', file)

    const response = await fetch(`${API_BASE_URL}/api/garments/analyze`, {
      method: 'POST',
      headers: authHeaders(),
      body,
    })

    return parseResponse<AnalyzeGarmentResponse>(response)
  },

  /** Creates the garment with the values the user confirmed. No AI call. */
  async confirmGarment(
    request: ConfirmGarmentRequest,
  ): Promise<CreateGarmentResponse> {
    const response = await fetch(`${API_BASE_URL}/api/garments/confirm`, {
      method: 'POST',
      headers: {
        ...authHeaders(),
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(request),
    })

    return parseResponse<CreateGarmentResponse>(response)
  },

  /** Public runtime flags (currently only the demo read-only switch). */
  async getConfig(): Promise<AppConfig> {
    const response = await fetch(`${API_BASE_URL}/api/config`, {
      headers: authHeaders(),
    })

    return parseResponse<AppConfig>(response)
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

  /** Saved outfits, already ordered by favorite and creation date. */
  async getOutfits(signal?: AbortSignal): Promise<Outfit[]> {
    const response = await fetch(`${API_BASE_URL}/api/outfits`, {
      headers: authHeaders(),
      signal,
    })

    return parseResponse<Outfit[]>(response)
  },

  async createOutfit(request: CreateOutfitRequest): Promise<Outfit> {
    const response = await fetch(`${API_BASE_URL}/api/outfits`, {
      method: 'POST',
      headers: {
        ...authHeaders(),
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(request),
    })

    return parseResponse<Outfit>(response)
  },

  async patchOutfit(
    id: string,
    request: PatchOutfitRequest,
  ): Promise<Outfit> {
    const response = await fetch(
      `${API_BASE_URL}/api/outfits/${encodeURIComponent(id)}`,
      {
        method: 'PATCH',
        headers: {
          ...authHeaders(),
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(request),
      },
    )

    return parseResponse<Outfit>(response)
  },

  /** Deletes an outfit. The API answers `204 No Content`. */
  async deleteOutfit(id: string): Promise<void> {
    const response = await fetch(
      `${API_BASE_URL}/api/outfits/${encodeURIComponent(id)}`,
      {
        method: 'DELETE',
        headers: authHeaders(),
      },
    )

    if (response.ok) {
      return
    }

    await parseResponse<never>(response)
  },

  /** AI (or fallback) proposals; nothing is persisted until `createOutfit`. */
  async suggestOutfits(
    request: SuggestOutfitsRequest,
  ): Promise<SuggestOutfitsResponse> {
    const response = await fetch(`${API_BASE_URL}/api/outfits/suggest`, {
      method: 'POST',
      headers: {
        ...authHeaders(),
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(request),
    })

    return parseResponse<SuggestOutfitsResponse>(response)
  },
}

export { API_BASE_URL }
