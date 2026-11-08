import { mediaUrl } from '../../lib/api'
import type { Garment } from '../../lib/types'
import { categoryLabel, seasonLabel } from './garmentLabels'

interface GarmentCardProps {
  garment: Garment
}

function needsReview(garment: Garment): boolean {
  return garment.confidence < 0.6
}

export function GarmentCard({ garment }: GarmentCardProps) {
  const seasonText = garment.seasons.map(seasonLabel).join(' · ')

  return (
    <article className="group flex h-full flex-col overflow-hidden rounded-2xl border border-stone-200 bg-white shadow-sm transition-shadow hover:shadow-md">
      <div className="relative aspect-square overflow-hidden bg-stone-100">
        <img
          src={mediaUrl(garment.thumbUrl)}
          alt={garment.name}
          width={400}
          height={400}
          loading="lazy"
          decoding="async"
          className="h-full w-full object-cover transition-transform duration-300 group-hover:scale-[1.02]"
        />
        {needsReview(garment) && (
          <span
            className="absolute top-2 left-2 rounded-full bg-white/90 px-2 py-0.5 text-[11px] font-medium text-amber-800 shadow-sm backdrop-blur"
            title={`Confianza del análisis: ${Math.round(garment.confidence * 100)}%`}
          >
            Revisar
          </span>
        )}
      </div>

      <div className="flex flex-1 flex-col gap-1.5 p-3 sm:p-4">
        <h2
          className="truncate text-sm font-medium text-stone-900"
          title={garment.name}
        >
          {garment.name}
        </h2>
        <p className="flex items-center gap-1.5 text-xs text-stone-600">
          <span
            aria-hidden="true"
            className="size-3 shrink-0 rounded-full border border-stone-300"
            style={{ backgroundColor: garment.colorHex }}
          />
          <span className="truncate">
            {garment.colorName} · {categoryLabel(garment.category)}
          </span>
        </p>
        <p className="truncate text-[11px] tracking-wide text-stone-400">
          {seasonText || '\u00a0'}
        </p>
      </div>
    </article>
  )
}

/** Mirrors the card structure so the grid never jumps when data arrives. */
export function GarmentCardSkeleton() {
  return (
    <div
      aria-hidden="true"
      className="flex h-full flex-col overflow-hidden rounded-2xl border border-stone-200 bg-white"
    >
      <div className="aspect-square animate-pulse bg-stone-200" />
      <div className="flex flex-1 flex-col gap-2 p-3 sm:p-4">
        <div className="h-4 w-3/4 animate-pulse rounded bg-stone-200" />
        <div className="h-3 w-1/2 animate-pulse rounded bg-stone-200" />
        <div className="h-3 w-2/3 animate-pulse rounded bg-stone-100" />
      </div>
    </div>
  )
}
