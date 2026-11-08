import { cleanup, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { Garment } from '../../lib/types'
import { OutfitBuilder } from './OutfitBuilder'

function garment(
  id: string,
  name: string,
  category: Garment['category'],
  formality: number,
): Garment {
  return {
    id,
    name,
    category,
    colorName: 'negro',
    colorHex: '#111111',
    formality,
    confidence: 0.9,
    seasons: ['spring'],
    styleTags: ['casual'],
    imageUrl: `/media/${id}.webp`,
    thumbUrl: `/media/${id}-thumb.webp`,
  }
}

const camisa = garment('camisa', 'Camisa de lino', 'top', 3)
const remera = garment('remera', 'Remera básica', 'top', 1)
const jean = garment('jean', 'Jean recto', 'bottom', 4)
const zapatillas = garment('zapatillas', 'Zapatillas blancas', 'shoes', 2)

const wardrobeFixture: Garment[] = [camisa, remera, jean, zapatillas]

function saveButton() {
  return screen.getByRole('button', { name: /guardar outfit/i })
}

afterEach(cleanup)

describe('OutfitBuilder', () => {
  it('disables save until the selection is a valid outfit', async () => {
    const user = userEvent.setup()
    render(<OutfitBuilder wardrobe={wardrobeFixture} onSave={vi.fn()} />)

    await user.click(screen.getByRole('checkbox', { name: /camisa/i }))
    expect(saveButton()).toBeDisabled()

    await user.click(screen.getByRole('checkbox', { name: /jean/i }))
    await user.click(screen.getByRole('checkbox', { name: /zapatillas/i }))
    expect(saveButton()).toBeEnabled()
  })

  it('rejects a formality spread greater than 2 points and shows the reason', async () => {
    const user = userEvent.setup()
    render(<OutfitBuilder wardrobe={wardrobeFixture} onSave={vi.fn()} />)

    await user.click(screen.getByRole('checkbox', { name: /remera/i }))
    await user.click(screen.getByRole('checkbox', { name: /jean/i }))
    await user.click(screen.getByRole('checkbox', { name: /zapatillas/i }))

    expect(saveButton()).toBeDisabled()
    expect(
      screen.getByText(
        'La formalidad de las prendas no puede variar más de 2 puntos.',
      ),
    ).toBeInTheDocument()
  })

  it('prefills name, occasion and garments and submits the draft', async () => {
    const user = userEvent.setup()
    const onSave = vi.fn()
    render(
      <OutfitBuilder
        wardrobe={wardrobeFixture}
        initialName="Look de trabajo"
        initialOccasion="trabajo"
        initialSelection={[camisa, jean, zapatillas]}
        onSave={onSave}
      />,
    )

    expect(screen.getByLabelText('Nombre')).toHaveValue('Look de trabajo')
    expect(screen.getByLabelText('Ocasión')).toHaveValue('trabajo')
    expect(screen.getByRole('checkbox', { name: /camisa/i })).toBeChecked()
    expect(saveButton()).toBeEnabled()

    await user.click(saveButton())

    expect(onSave).toHaveBeenCalledWith({
      name: 'Look de trabajo',
      occasion: 'trabajo',
      garmentIds: ['camisa', 'jean', 'zapatillas'],
    })
  })

  it('keeps an archived garment from a saved outfit without offering it again', () => {
    const archivedShoes = garment(
      'archivadas',
      'Zapatillas viejas',
      'shoes',
      2,
    )

    render(
      <OutfitBuilder
        wardrobe={[camisa, jean]}
        initialSelection={[camisa, jean, archivedShoes]}
        onSave={vi.fn()}
      />,
    )

    expect(screen.getByText('Zapatillas viejas')).toBeInTheDocument()
    expect(screen.getAllByText(/archivada/i).length).toBeGreaterThan(0)
    expect(
      screen.queryByRole('checkbox', { name: /zapatillas viejas/i }),
    ).not.toBeInTheDocument()
    expect(saveButton()).toBeEnabled()
  })
})
