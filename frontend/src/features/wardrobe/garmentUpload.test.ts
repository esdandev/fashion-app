import { describe, expect, it } from 'vitest'
import type { Garment, GarmentAnalysisProposal } from '../../lib/types'
import {
  MAX_GARMENT_FILE_BYTES,
  garmentFormValuesFromCurrentGarment,
  garmentFormValuesFromGarment,
  garmentFormValuesFromProposal,
  splitTags,
  toConfirmRequest,
  toPatchRequest,
  validateGarmentFile,
} from './garmentUpload'

function file(name: string, type: string, size: number) {
  return { name, type, size }
}

const garment: Garment = {
  id: '1',
  name: 'Camisa de lino',
  category: 'top',
  colorName: 'beige',
  colorHex: '',
  formality: 3,
  confidence: 0.9,
  seasons: ['spring', 'summer'],
  styleTags: ['minimalista', 'casual'],
  imageUrl: '/media/hash.webp',
  thumbUrl: '/media/hash-thumb.webp',
}

describe('validateGarmentFile', () => {
  it('accepts the supported types at exactly 10 MB', () => {
    expect(
      validateGarmentFile(file('camisa.jpg', 'image/jpeg', MAX_GARMENT_FILE_BYTES)),
    ).toBeNull()
  })

  it('rejects files over 10 MB with a Spanish message', () => {
    expect(
      validateGarmentFile(
        file('camisa.jpg', 'image/jpeg', MAX_GARMENT_FILE_BYTES + 1),
      ),
    ).toBe('La imagen no puede superar los 10 MB.')
  })

  it('rejects unsupported formats', () => {
    expect(validateGarmentFile(file('camisa.gif', 'image/gif', 10))).toBe(
      'Solo se aceptan imágenes JPEG, PNG o WebP.',
    )
  })

  it('falls back to the extension when the browser omits the MIME type', () => {
    expect(validateGarmentFile(file('camisa.WEBP', '', 10))).toBeNull()
  })
})

describe('garment form mapping', () => {
  it('maps a garment to editable values', () => {
    expect(garmentFormValuesFromGarment(garment)).toEqual({
      name: 'Camisa de lino',
      category: 'top',
      colorName: 'beige',
      colorHex: '',
      seasons: ['spring', 'summer'],
      formality: 3,
      styleTags: 'minimalista, casual',
    })
  })

  it('applies a proposal over the current values and keeps fallbacks', () => {
    const proposal: GarmentAnalysisProposal = {
      name: '',
      category: 'not-a-category',
      subcategory: null,
      colors: [],
      pattern: null,
      seasons: ['winter', 'not-a-season'],
      formality: 9,
      material: null,
      styleTags: ['formal'],
      confidence: 0.4,
    }

    expect(
      garmentFormValuesFromProposal(proposal, garmentFormValuesFromGarment(garment)),
    ).toEqual({
      name: 'Camisa de lino',
      category: 'top',
      colorName: 'beige',
      colorHex: '',
      seasons: ['winter'],
      formality: 5,
      styleTags: 'formal',
    })
  })

  it('canonicalizes mixed-case categories and seasons from a proposal', () => {
    const proposal: GarmentAnalysisProposal = {
      name: 'Camisa',
      category: 'Top',
      subcategory: null,
      colors: ['Azul'],
      pattern: null,
      seasons: ['Winter'],
      formality: 3,
      material: null,
      styleTags: [],
      confidence: 0.8,
    }

    expect(
      garmentFormValuesFromProposal(proposal, garmentFormValuesFromGarment(garment)),
    ).toMatchObject({
      category: 'top',
      seasons: ['winter'],
      colorName: 'Azul',
    })
  })

  it('normalizes tags and clamps formality when building the PATCH body', () => {
    expect(
      toPatchRequest({
        name: '  Camisa vieja  ',
        category: 'top',
        colorName: ' blanco ',
        colorHex: ' #ffffff ',
        seasons: ['spring'],
        formality: 0,
        styleTags: ' casual, , minimalista ',
      }),
    ).toEqual({
      name: 'Camisa vieja',
      category: 'top',
      colorName: 'blanco',
      colorHex: '#ffffff',
      seasons: ['spring'],
      formality: 1,
      styleTags: ['casual', 'minimalista'],
    })

    expect(splitTags('')).toEqual([])
  })

  it('omits an empty colorHex so it cannot erase a saved swatch', () => {
    const patch = toPatchRequest({
      name: 'Camisa',
      category: 'top',
      colorName: 'beige',
      colorHex: '   ',
      seasons: [],
      formality: 3,
      styleTags: '',
    })
    expect(patch).not.toHaveProperty('colorHex')

    const confirm = toConfirmRequest(
      {
        name: 'Camisa',
        category: 'top',
        colorName: 'beige',
        colorHex: '',
        seasons: [],
        formality: 3,
        styleTags: '',
      },
      'a'.repeat(64),
    )
    expect(confirm).not.toHaveProperty('colorHex')
  })

  it('opens a re-uploaded photo with its current confirmed values', () => {
    expect(
      garmentFormValuesFromCurrentGarment({
        id: '1',
        name: 'Camisa editada',
        category: 'outerwear',
        subcategory: 'camisa',
        colorName: 'verde',
        colorHex: '#00aa55',
        pattern: 'liso',
        seasons: ['winter'],
        formality: 4,
        material: 'lino',
        styleTags: ['formal'],
        notes: null,
      }),
    ).toEqual({
      name: 'Camisa editada',
      category: 'outerwear',
      colorName: 'verde',
      colorHex: '#00aa55',
      seasons: ['winter'],
      formality: 4,
      styleTags: 'formal',
    })
  })

  it('builds a confirm request with the analyzed extras and confidence', () => {
    expect(
      toConfirmRequest(
        {
          name: '  Camisa de lino  ',
          category: 'top',
          colorName: ' celeste ',
          colorHex: ' #abcdef ',
          seasons: ['spring'],
          formality: 7,
          styleTags: ' casual, minimalista ',
        },
        'a'.repeat(64),
        {
          confidence: 0.9,
          subcategory: 'camisa',
          pattern: 'liso',
          material: 'lino',
        },
      ),
    ).toEqual({
      contentHash: 'a'.repeat(64),
      name: 'Camisa de lino',
      category: 'top',
      subcategory: 'camisa',
      colorName: 'celeste',
      colorHex: '#abcdef',
      pattern: 'liso',
      seasons: ['spring'],
      formality: 5,
      material: 'lino',
      styleTags: ['casual', 'minimalista'],
      notes: null,
      confidence: 0.9,
    })
  })
})
