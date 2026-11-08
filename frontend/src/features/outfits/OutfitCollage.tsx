import { mediaUrl } from '../../lib/api'
import type { OutfitItem } from '../../lib/types'

/** At most five photos: the first one is the hero, the rest fill the column. */
const MAX_VISIBLE = 5

interface OutfitCollageProps {
  items: OutfitItem[]
  name: string
  className?: string
}

function byDisplayOrder(left: OutfitItem, right: OutfitItem): number {
  return left.sortOrder - right.sortOrder || left.layer - right.layer
}

/**
 * Stable photo grid for a saved outfit: the first garment is the large hero
 * and up to four secondary photos stack on the right. The frame keeps a fixed
 * 4:3 ratio, and every image is lazy-loaded with explicit dimensions so the
 * list never shifts while the photos arrive.
 */
export function OutfitCollage({
  items,
  name,
  className = '',
}: OutfitCollageProps) {
  const ordered = [...items].sort(byDisplayOrder)
  const visible = ordered.slice(0, MAX_VISIBLE)
  const hiddenCount = ordered.length - visible.length
  const [primary, ...secondary] = visible

  if (!primary) {
    return (
      <div
        className={`flex aspect-[4/3] items-center justify-center rounded-2xl border border-dashed border-stone-300 bg-stone-100 text-xs text-stone-400 ${className}`}
      >
        Sin prendas
      </div>
    )
  }

  return (
    <div
      role="group"
      aria-label={name}
      className={`overflow-hidden rounded-2xl border border-stone-200 bg-stone-100 ${className}`}
    >
      <div
        className={`grid aspect-[4/3] gap-1 p-1 ${
          secondary.length > 0 ? 'grid-cols-[3fr_2fr]' : 'grid-cols-1'
        }`}
      >
        <div className="relative min-h-0 overflow-hidden rounded-xl bg-stone-200">
          <img
            src={mediaUrl(primary.garment.thumbUrl)}
            alt={primary.garment.name}
            width={640}
            height={480}
            loading="lazy"
            decoding="async"
            className="h-full w-full object-cover"
          />
        </div>

        {secondary.length > 0 && (
          <div className="flex min-h-0 flex-col gap-1">
            {secondary.map((item, index) => {
              const isLast = index === secondary.length - 1
              return (
                <div
                  key={`${item.garment.id}-${item.sortOrder}`}
                  className="relative min-h-0 flex-1 overflow-hidden rounded-xl bg-stone-200"
                >
                  <img
                    src={mediaUrl(item.garment.thumbUrl)}
                    alt={item.garment.name}
                    width={320}
                    height={240}
                    loading="lazy"
                    decoding="async"
                    className="h-full w-full object-cover"
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
        )}
      </div>
    </div>
  )
}
