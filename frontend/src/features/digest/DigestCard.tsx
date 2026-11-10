import { mediaUrl } from '../../lib/api'
import type { DigestItemDto } from '../../lib/types'

interface DigestCardProps {
  item: DigestItemDto
  /** Position in the daily list, rendered as an editorial folio. */
  index: number
  /** The first note carries the main digest image, so it loads eagerly. */
  priority?: boolean
}

/** One digest note laid out as an editorial row, not a card. */
export function DigestCard({ item, index, priority = false }: DigestCardProps) {
  const relatedGarments = item.relatedGarments.slice(0, 3)

  return (
    <article className="group grid gap-4 py-6 sm:grid-cols-[38px_1fr_174px] sm:gap-6">
      <span
        aria-hidden="true"
        className="font-display text-3xl leading-none text-oxblood"
      >
        {String(index + 1).padStart(2, '0')}
      </span>

      <div className="min-w-0">
        <p className="editorial-kicker">{item.source}</p>
        <h3 className="mt-1 max-w-[34ch] font-display text-[26px] leading-[1.05] font-normal tracking-[-0.035em] text-ink sm:text-[30px]">
          <a
            href={item.url}
            target="_blank"
            rel="noopener noreferrer"
            className="underline-offset-4 transition-colors hover:text-oxblood hover:underline focus-visible:outline-none"
          >
            {item.title}
            <span className="sr-only"> (abre en una pestaña nueva)</span>
          </a>
        </h3>
        <p className="mt-3 max-w-[62ch] font-serif text-sm leading-relaxed text-stone-600">
          {item.summary}
        </p>

        {item.styleTags.length > 0 && (
          <ul aria-label="Etiquetas de estilo" className="mt-3 flex flex-wrap gap-1.5">
            {item.styleTags.map((tag) => (
              <li key={tag} className="editorial-tag">
                {tag}
              </li>
            ))}
          </ul>
        )}
      </div>

      <div className="flex items-start gap-3 sm:flex-col sm:items-end">
        {item.imageUrl && (
          <img
            src={mediaUrl(item.imageUrl)}
            alt=""
            width={800}
            height={450}
            loading={priority ? 'eager' : 'lazy'}
            decoding="async"
            fetchPriority={priority ? 'high' : 'auto'}
            className="h-24 w-40 border border-stone-300/80 bg-stone-100 object-cover sm:h-[104px] sm:w-[174px]"
          />
        )}
        {relatedGarments.length > 0 && (
          <div className="sm:w-full">
            <p className="editorial-meta text-[10px] uppercase">
              En tu guardarropa
            </p>
            <ul className="mt-1.5 flex flex-wrap gap-1.5 sm:justify-end">
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
                    className="size-12 border border-stone-300/80 bg-stone-100 object-cover"
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

/** Mirrors the row structure so the list never jumps when data arrives. */
export function DigestCardSkeleton() {
  return (
    <div
      aria-hidden="true"
      className="grid gap-4 py-6 sm:grid-cols-[38px_1fr_174px] sm:gap-6"
    >
      <div className="h-6 w-7 animate-pulse bg-stone-200" />
      <div>
        <div className="h-3 w-20 animate-pulse bg-stone-200" />
        <div className="mt-2 h-6 w-11/12 animate-pulse bg-stone-200" />
        <div className="mt-2 h-6 w-2/3 animate-pulse bg-stone-200" />
        <div className="mt-3 h-4 w-full animate-pulse bg-stone-100" />
        <div className="mt-1.5 h-4 w-4/5 animate-pulse bg-stone-100" />
      </div>
      <div className="hidden h-[104px] w-[174px] animate-pulse bg-stone-100 sm:block" />
    </div>
  )
}
