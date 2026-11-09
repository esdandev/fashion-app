import { mediaUrl } from '../../lib/api'
import type { OutfitItem } from '../../lib/types'

/** At most five garments, stacked from top to bottom like a mannequin. */
const MAX_VISIBLE = 5

const CATEGORY_RANK: Record<string, number> = {
  outerwear: 0,
  dress: 1,
  top: 2,
  bottom: 3,
  shoes: 4,
  bag: 5,
  accessory: 6,
}

interface OutfitCollageProps {
  items: OutfitItem[]
  name: string
  className?: string
}

/**
 * Visual order on the mannequin: outerwear first, then the base piece,
 * bottoms, shoes and finally bags/accessories. Within a category the order
 * chosen by the user wins.
 */
function byMannequinOrder(left: OutfitItem, right: OutfitItem): number {
  const rank =
    (CATEGORY_RANK[left.garment.category] ?? 99) -
    (CATEGORY_RANK[right.garment.category] ?? 99)

  return rank || left.sortOrder - right.sortOrder || left.layer - right.layer
}

/**
 * A portrait "mannequin" instead of a square photo grid: each garment is a
 * full-width band, stacked head-to-toe. The frame keeps a fixed 2:3 ratio and
 * every image is lazy-loaded with explicit dimensions so the outfit list never
 * shifts while the photos arrive.
 */
export function OutfitCollage({
  items,
  name,
  className = '',
}: OutfitCollageProps) {
  const ordered = [...items].sort(byMannequinOrder)
  const visible = ordered.slice(0, MAX_VISIBLE)
  const hiddenCount = ordered.length - visible.length

  if (visible.length === 0) {
    return (
      <div
        className={`flex aspect-[2/3] items-center justify-center rounded-2xl border border-dashed border-stone-300 bg-stone-100 text-xs text-stone-400 ${className}`}
      >
        Sin prendas
      </div>
    )
  }

  return (
    <div
      role="group"
      aria-label={name}
      data-testid="outfit-mannequin"
      className={`relative aspect-[2/3] overflow-hidden rounded-2xl border border-stone-200 bg-gradient-to-b from-stone-50 via-white to-stone-100 ${className}`}
    >
      {/* A subtle vertical guide gives the stack its mannequin silhouette. */}
      <div
        aria-hidden="true"
        className="pointer-events-none absolute inset-y-5 left-1/2 w-px -translate-x-1/2 bg-stone-200/80"
      />

      <div className="relative flex h-full flex-col">
        {visible.map((item, index) => {
          const isLast = index === visible.length - 1
          return (
            <div
              key={`${item.garment.id}-${item.sortOrder}`}
              className="relative min-h-0 flex-1 overflow-hidden border-b border-stone-200/60 last:border-b-0"
            >
              <img
                src={mediaUrl(item.garment.thumbUrl)}
                alt={item.garment.name}
                width={600}
                height={600}
                loading="lazy"
                decoding="async"
                className="h-full w-full object-contain p-1"
              />
              {isLast && hiddenCount > 0 && (
                <span
                  className="absolute inset-0 flex items-center justify-center bg-stone-950/55 text-sm font-semibold text-white"
                  aria-label={`${hiddenCount} prendas más`}
                >
                  +{hiddenCount}
                </span>
              )}
            </div>
          )
        })}
      </div>
    </div>
  )
}
