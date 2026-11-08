import { mediaUrl } from '../../lib/api'
import type { DigestItemDto } from '../../lib/types'

interface DigestCardProps {
  item: DigestItemDto
  /** The first note carries the main digest image, so it loads eagerly. */
  priority?: boolean
}

/** One digest note: image, source link, summary, style tags and matching garments. */
export function DigestCard({ item, priority = false }: DigestCardProps) {
  const relatedGarments = item.relatedGarments.slice(0, 3)

  return (
    <article className="flex h-full flex-col overflow-hidden rounded-2xl border border-stone-200 bg-white shadow-sm transition-shadow hover:shadow-md">
      {item.imageUrl ? (
        <img
          src={mediaUrl(item.imageUrl)}
          alt=""
          width={800}
          height={450}
          loading={priority ? 'eager' : 'lazy'}
          decoding="async"
          fetchPriority={priority ? 'high' : 'auto'}
          className="aspect-[16/9] w-full bg-stone-100 object-cover"
        />
      ) : (
        <div
          aria-hidden="true"
          className="aspect-[16/9] w-full bg-gradient-to-br from-stone-200 via-stone-100 to-amber-50"
        />
      )}

      <div className="flex flex-1 flex-col gap-3 p-4 sm:p-5">
        <div>
          <h3 className="font-display text-lg leading-snug font-semibold text-stone-900">
            <a
              href={item.url}
              target="_blank"
              rel="noopener noreferrer"
              className="underline-offset-4 transition-colors hover:text-stone-600 hover:underline focus-visible:ring-2 focus-visible:ring-stone-400 focus-visible:ring-offset-2 focus-visible:outline-none"
            >
              {item.title}
              <span className="sr-only"> (abre en una pestaña nueva)</span>
            </a>
          </h3>
          <p className="mt-1 text-[11px] font-medium tracking-widest text-stone-400 uppercase">
            {item.source}
          </p>
        </div>

        <p className="text-sm leading-relaxed text-stone-600">{item.summary}</p>

        {item.styleTags.length > 0 && (
          <ul aria-label="Etiquetas de estilo" className="flex flex-wrap gap-1.5">
            {item.styleTags.map((tag) => (
              <li
                key={tag}
                className="rounded-full bg-stone-100 px-2.5 py-0.5 text-[11px] font-medium text-stone-600"
              >
                {tag}
              </li>
            ))}
          </ul>
        )}

        {relatedGarments.length > 0 && (
          <div className="mt-auto border-t border-stone-100 pt-3">
            <p className="text-[10px] font-medium tracking-widest text-stone-400 uppercase">
              En tu guardarropa
            </p>
            <ul className="mt-2 flex flex-wrap gap-2">
              {relatedGarments.map((garment) => (
                <li key={garment.id}>
                  <img
                    src={mediaUrl(garment.thumbUrl)}
                    alt={garment.name}
                    title={garment.name}
                    width={400}
                    height={400}
                    loading="lazy"
                    decoding="async"
                    className="size-14 rounded-xl border border-stone-200 bg-stone-100 object-cover"
                  />
                </li>
              ))}
            </ul>
          </div>
        )}
      </div>
    </article>
  )
}

/** Mirrors the card structure so the grid never jumps when data arrives. */
export function DigestCardSkeleton() {
  return (
    <div
      aria-hidden="true"
      className="flex h-full flex-col overflow-hidden rounded-2xl border border-stone-200 bg-white"
    >
      <div className="aspect-[16/9] w-full animate-pulse bg-stone-200" />
      <div className="flex flex-1 flex-col gap-3 p-4 sm:p-5">
        <div className="h-3 w-20 animate-pulse rounded bg-stone-100" />
        <div className="h-5 w-11/12 animate-pulse rounded bg-stone-200" />
        <div className="h-4 w-full animate-pulse rounded bg-stone-100" />
        <div className="h-4 w-4/5 animate-pulse rounded bg-stone-100" />
        <div className="mt-1 flex gap-1.5">
          <div className="h-5 w-16 animate-pulse rounded-full bg-stone-100" />
          <div className="h-5 w-14 animate-pulse rounded-full bg-stone-100" />
        </div>
      </div>
    </div>
  )
}
