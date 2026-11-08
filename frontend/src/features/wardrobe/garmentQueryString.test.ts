import { describe, expect, it } from 'vitest'
import { buildGarmentQuery } from './garmentQueryString'

describe('buildGarmentQuery', () => {
  it('omits empty filters and encodes values', () => {
    expect(buildGarmentQuery({ category: 'top', color: '', season: 'summer' }))
      .toBe('?category=top&season=summer')
  })

  it('returns an empty string when nothing is selected', () => {
    expect(buildGarmentQuery({ category: '', color: '  ', season: undefined }))
      .toBe('')
  })

  it('trims values and encodes them', () => {
    expect(buildGarmentQuery({ color: ' azul marino ' })).toBe(
      '?color=azul+marino',
    )
  })
})
