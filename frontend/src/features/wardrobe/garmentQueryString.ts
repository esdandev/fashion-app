export interface GarmentQueryFilters {
  category?: string
  color?: string
  season?: string
  cursor?: string | null
}

const FILTER_KEYS = ['category', 'color', 'season', 'cursor'] as const

/**
 * Builds the `/api/garments` query string. Empty or whitespace-only filters are
 * omitted so switching a filter back to "todas" never leaves `category=` behind.
 */
export function buildGarmentQuery(filters: GarmentQueryFilters): string {
  const params = new URLSearchParams()

  for (const key of FILTER_KEYS) {
    const value = filters[key]?.trim()
    if (value) {
      params.set(key, value)
    }
  }

  const query = params.toString()
  return query ? `?${query}` : ''
}
