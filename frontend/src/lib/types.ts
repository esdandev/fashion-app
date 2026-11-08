/** Shapes returned by the Fashion App API (see Features/Garments contracts). */

export type GarmentCategory =
  | 'top'
  | 'bottom'
  | 'dress'
  | 'outerwear'
  | 'shoes'
  | 'bag'
  | 'accessory'

export type Season = 'spring' | 'summer' | 'autumn' | 'winter'

export interface Garment {
  id: string
  name: string
  category: GarmentCategory
  colorName: string
  colorHex: string
  formality: number
  confidence: number
  seasons: Season[]
  styleTags: string[]
  imageUrl: string
  thumbUrl: string
}

export interface GarmentListResponse {
  items: Garment[]
  nextCursor: string | null
}

export interface GarmentFiltersState {
  category: GarmentCategory | ''
  season: Season | ''
}

export interface GarmentAnalysisProposal {
  name: string
  category: string
  subcategory: string | null
  colors: string[]
  pattern: string | null
  seasons: string[]
  formality: number
  material: string | null
  styleTags: string[]
  confidence: number
}

export interface CreateGarmentResponse {
  garment: Garment
  deduplicated: boolean
}

/** RFC 7807 payload the API returns for validation and auth errors. */
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
}
