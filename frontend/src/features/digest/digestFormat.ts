/**
 * Formats the digest `DateOnly` (YYYY-MM-DD) as a long Spanish date, e.g.
 * `"2026-11-08"` → `"domingo 8 de noviembre"`.
 */
export function formatDigestDate(date: string): string {
  const formatted = new Intl.DateTimeFormat('es-AR', {
    weekday: 'long',
    day: 'numeric',
    month: 'long',
    timeZone: 'UTC',
  }).format(new Date(`${date}T00:00:00Z`))

  // es-AR inserts a comma between the weekday and the date; the hero reads
  // better without it.
  return formatted.replace(/,/g, '')
}
