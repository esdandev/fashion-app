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

/** Current confirmed values for an already saved photo. */
export interface CurrentGarmentValues {
  id: string
  name: string
  category: string
  subcategory: string | null
  colorName: string
  colorHex: string
  pattern: string | null
  seasons: string[]
  formality: number
  material: string | null
  styleTags: string[]
  notes: string | null
}

/** Result of `POST /api/garments/analyze`; no garment row exists yet. */
export interface AnalyzeGarmentResponse {
  contentHash: string
  imageUrl: string
  thumbUrl: string
  analysis: GarmentAnalysisProposal
  existingGarmentId: string | null
  currentGarment: CurrentGarmentValues | null
}

/** Mirrors `ConfirmGarmentRequest` on the API. */
export interface ConfirmGarmentRequest {
  contentHash: string
  name: string
  category: GarmentCategory
  subcategory?: string | null
  colorName: string
  colorHex?: string
  pattern?: string | null
  seasons: Season[]
  formality: number
  material?: string | null
  styleTags: string[]
  notes?: string | null
  confidence?: number
}

export interface CreateGarmentResponse {
  garment: Garment
  deduplicated: boolean
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

/** RFC 7807 payload the API returns for validation and auth errors. */
export interface ProblemDetails {
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
}
