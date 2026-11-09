import { describe, expect, it } from 'vitest'
import type { Garment } from '../../lib/types'
import {
  EMPTY_GARMENT_FORM_VALUES,
  garmentFormValuesFromGarment,
  garmentValuesFromFile,
  validateGarmentFile,
  validateGarmentValues,
} from './garmentUpload'

function file(name: string, size = 1024, type = 'image/jpeg'): File {
  return { name, size, type } as File
}

const garment: Garment = {
  id: '1',
  name: 'Camisa de lino',
  category: 'top',
  colorName: 'beige',
  colorHex: '#d8c3a5',
  formality: 3,
  confidence: 1,
  seasons: ['spring', 'summer'],
  styleTags: ['minimalista'],
  imageUrl: '/media/a.webp',
  thumbUrl: '/media/a-thumb.webp',
}

describe('garmentUpload', () => {
  it('accepts JPEG, PNG and WebP files under 10 MB', () => {
    expect(validateGarmentFile(file('camisa.jpg'))).toBeNull()
    expect(validateGarmentFile(file('camisa.png', 1024, 'image/png'))).toBeNull()
    expect(validateGarmentFile(file('camisa.webp', 1024, 'image/webp'))).toBeNull()
  })

  it('rejects unsupported formats and oversized files', () => {
    expect(validateGarmentFile(file('camisa.gif', 1024, 'image/gif'))).toBe(
      'Solo se aceptan imágenes JPEG, PNG o WebP.',
    )
    expect(validateGarmentFile(file('grande.jpg', 10 * 1024 * 1024 + 1))).toBe(
      'La imagen no puede superar los 10 MB.',
    )
  })

  it('derives a readable name from the file name', () => {
    expect(garmentValuesFromFile(file('camisa-de-lino.jpg')).name).toBe(
      'camisa de lino',
    )
    expect(garmentValuesFromFile(file('.jpg')).name).toBe('Prenda')
  })

  it('validates the manual attributes', () => {
    expect(validateGarmentValues(EMPTY_GARMENT_FORM_VALUES)).toBe(
      'El nombre no puede estar vacío.',
    )
    expect(
      validateGarmentValues({ ...EMPTY_GARMENT_FORM_VALUES, name: 'Camisa' }),
    ).toBeNull()
    expect(
      validateGarmentValues({
        ...EMPTY_GARMENT_FORM_VALUES,
        name: 'Camisa',
        colorHex: 'beige',
      }),
    ).toBe('El color hexadecimal debe tener el formato #RRGGBB.')
    expect(
      validateGarmentValues({
        ...EMPTY_GARMENT_FORM_VALUES,
        name: 'Camisa',
        formality: 9,
      }),
    ).toBe('La formalidad debe estar entre 1 y 5.')
  })

  it('maps an existing garment back to the form', () => {
    expect(garmentFormValuesFromGarment(garment)).toEqual({
      name: 'Camisa de lino',
      category: 'top',
      colorName: 'beige',
      colorHex: '#d8c3a5',
      seasons: ['spring', 'summer'],
      formality: 3,
      styleTags: ['minimalista'],
    })
  })
})
