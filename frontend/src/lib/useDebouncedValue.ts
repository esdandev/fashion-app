import { useEffect, useState } from 'react'

/**
 * Delays propagating a fast-changing value (like the color filter) so the
 * wardrobe does not fire one request per keystroke.
 */
export function useDebouncedValue<T>(value: T, delay = 300): T {
  const [debounced, setDebounced] = useState(value)

  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delay)
    return () => clearTimeout(timer)
  }, [value, delay])

  return debounced
}
