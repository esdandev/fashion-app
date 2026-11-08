import { describe, expect, it } from 'vitest'
import { formatDigestDate } from './digestFormat'

describe('formatDigestDate', () => {
  it('formats an ISO date in Spanish', () => {
    expect(formatDigestDate('2026-11-08')).toBe('domingo 8 de noviembre')
  })
})
