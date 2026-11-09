import { cleanup, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it } from 'vitest'
import type { Garment, OutfitItem } from '../../lib/types'
import { OutfitCollage } from './OutfitCollage'

function garment(
  id: string,
  name: string,
  category: Garment['category'],
): Garment {
  return {
    id,
    name,
    category,
    colorName: 'negro',
    colorHex: '#111111',
    formality: 3,
    confidence: 0.9,
    seasons: ['spring'],
    styleTags: ['casual'],
    imageUrl: `/media/${id}.webp`,
    thumbUrl: `/media/${id}-thumb.webp`,
  }
}

function item(
  id: string,
  name: string,
  category: Garment['category'],
  sortOrder: number,
): OutfitItem {
  return {
    garment: garment(id, name, category),
    layer: category === 'outerwear' ? 1 : 0,
    sortOrder,
  }
}

afterEach(cleanup)

describe('OutfitCollage', () => {
  it('stacks the garments head-to-toe like a mannequin', () => {
    const items = [
      item('zapatillas', 'Zapatillas', 'shoes', 4),
      item('jean', 'Jean', 'bottom', 3),
      item('campera', 'Campera', 'outerwear', 1),
      item('camisa', 'Camisa', 'top', 2),
      item('bolso', 'Bolso', 'bag', 5),
    ]

    render(<OutfitCollage items={items} name="Look de prueba" />)

    expect(screen.getByTestId('outfit-mannequin')).toBeInTheDocument()
    expect(screen.getAllByRole('img').map((image) => image.getAttribute('alt'))).toEqual([
      'Campera',
      'Camisa',
      'Jean',
      'Zapatillas',
      'Bolso',
    ])
  })

  it('shows an empty frame when there are no garments', () => {
    render(<OutfitCollage items={[]} name="Vacío" />)
    expect(screen.getByText('Sin prendas')).toBeInTheDocument()
  })
})
