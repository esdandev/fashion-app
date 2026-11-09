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
  color: string
  season: Season | ''
}

export interface CreateGarmentResponse {
  garment: Garment
  deduplicated: boolean
}

/** Manual attributes sent with a garment photo. */
export interface CreateGarmentInput {
  name: string
  category: GarmentCategory
  colorName: string
  colorHex: string
  seasons: Season[]
  formality: number
  styleTags: string[]
}

/** Public runtime flags used to hide write actions in the demo. */
export interface AppConfig {
  demo: boolean
}

/** Mirrors `PatchGarmentRequest` on the API. Only sent fields are changed. */
export interface PatchGarmentRequest {
  name?: string
  category?: GarmentCategory
  colorName?: string
  colorHex?: string
  formality?: number
  seasons?: Season[]
  styleTags?: string[]
}

export type OutfitSource = 'ai' | 'manual'

/** One garment in a saved outfit, in display order. */
export interface OutfitItem {
  garment: Garment
  layer: number
  sortOrder: number
}

/** Mirrors `OutfitDto` on the API. */
export interface Outfit {
  id: string
  name: string
  occasion: string | null
  source: OutfitSource
  rationale: string | null
  isFavorite: boolean
  createdAt: string
  items: OutfitItem[]
}

/** Mirrors `CreateOutfitRequest`; item order becomes the collage order. */
export interface CreateOutfitRequest {
  name: string
  occasion?: string | null
  garmentIds: string[]
  /** Defaults to `manual` on the API. Saving an AI proposal sends `ai`. */
  source?: OutfitSource
  /** Optional explanation kept with an AI-saved outfit. */
  rationale?: string | null
}

/** Mirrors `PatchOutfitRequest`. Only sent fields are changed. */
export interface PatchOutfitRequest {
  name?: string
  occasion?: string | null
  isFavorite?: boolean
  garmentIds?: string[]
}

/** Manual weather selection; matches the values normalized by the API. */
export type OutfitWeather = 'frio' | 'templado' | 'calor'

/** Mirrors `SuggestOutfitsRequest`. */
export interface SuggestOutfitsRequest {
  occasion?: string | null
  weather: OutfitWeather
}

/** A proposal that has not been saved yet. */
export interface OutfitDraft {
  name: string
  garmentIds: string[]
  occasion: string | null
  rationale: string
}

/** Mirrors `SuggestOutfitsResponse`. */
export interface SuggestOutfitsResponse {
  outfits: OutfitDraft[]
}

/** One news note with its own Spanish summary and, when allowed, matching garments. */
export interface DigestItemDto {
  id: string
  title: string
  url: string
  source: string
  summary: string
  styleTags: string[]
  imageUrl: string | null
  publishedAt: string | null
  /** Empty for anonymous requests on a token-protected instance. */
  relatedGarments: Garment[]
}

/** Mirrors `DigestDto`, the read model of `GET /api/digest/today`. */
export interface DigestDto {
  /** `DateOnly` serialized as YYYY-MM-DD. */
  date: string
  summary: string
  generatedAt: string
  items: DigestItemDto[]
}

/** RFC 7807 payload the API returns for validation and auth errors. */
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
}
