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
    expect(
      screen.getByRole('button', { name: 'Quitar Zapatillas viejas' }),
    ).toBeInTheDocument()
    expect(saveButton()).toBeEnabled()
  })

  it('removes an archived garment and cannot offer it again', async () => {
    const user = userEvent.setup()
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

    await user.click(
      screen.getByRole('button', { name: 'Quitar Zapatillas viejas' }),
    )

    expect(screen.queryByText('Zapatillas viejas')).not.toBeInTheDocument()
    expect(
      screen.queryByRole('button', { name: 'Quitar Zapatillas viejas' }),
    ).not.toBeInTheDocument()
    // The outfit is now invalid (it lost its shoes) and there is no way to
    // bring the archived garment back.
    expect(saveButton()).toBeDisabled()
    expect(screen.getByText('El outfit necesita calzado.')).toBeInTheDocument()
  })

  it('validates only the active selection that will be submitted', async () => {
    const user = userEvent.setup()
    const onSave = vi.fn()
    const archivedShoes = garment(
      'archivadas',
      'Zapatillas viejas',
      'shoes',
      2,
    )

    render(
      <OutfitBuilder
        wardrobe={[camisa, jean, zapatillas]}
        initialSelection={[camisa, jean, archivedShoes]}
        initialName="Look de trabajo"
        onSave={onSave}
      />,
    )

    expect(saveButton()).toBeEnabled()
    expect(screen.queryByRole('note')).not.toBeInTheDocument()

    // Changing the selection makes the archived shoes disappear on save, so the
    // validation must run without them: the outfit loses its bottom.
    await user.click(screen.getByRole('checkbox', { name: /jean/i }))
    expect(screen.getByRole('note')).toHaveTextContent(
      'Al guardar se quitarán las prendas archivadas: ya no están en el guardarropa.',
    )
    expect(saveButton()).toBeDisabled()
    expect(
      screen.getByText(
        'El outfit necesita una parte superior y una inferior, o un vestido.',
      ),
    ).toBeInTheDocument()

    // Restoring the exact initial selection keeps the archived shoes again.
    await user.click(screen.getByRole('checkbox', { name: /jean/i }))
    expect(screen.queryByRole('note')).not.toBeInTheDocument()
    expect(saveButton()).toBeEnabled()

    // Adding an active shoe is valid on its own and the archived one is dropped
    // from the submitted ids.
    await user.click(screen.getByRole('checkbox', { name: /zapatillas/i }))
    expect(screen.getByRole('note')).toBeInTheDocument()
    expect(saveButton()).toBeEnabled()

    await user.click(saveButton())
    expect(onSave).toHaveBeenCalledWith({
      name: 'Look de trabajo',
      occasion: null,
      garmentIds: ['camisa', 'jean', 'zapatillas'],
    })
  })

  it('disables save when dropping an archived garment leaves the outfit invalid', async () => {
    const user = userEvent.setup()
    const remera = garment('remera', 'Remera básica', 'top', 1)
    const archivedShoes = garment(
      'archivadas',
      'Zapatillas viejas',
      'shoes',
      2,
    )

    render(
      <OutfitBuilder
        wardrobe={[camisa, remera, jean]}
        initialSelection={[camisa, jean, archivedShoes]}
        onSave={vi.fn()}
      />,
    )

    // The saved outfit is valid thanks to its archived shoes.
    expect(saveButton()).toBeEnabled()

    // Swapping the active top changes the selection: the archived shoes will be
    // dropped, and the resulting top+bottom has no shoes.
    await user.click(screen.getByRole('checkbox', { name: /camisa/i }))
    await user.click(screen.getByRole('checkbox', { name: /remera/i }))

    expect(screen.getByRole('note')).toBeInTheDocument()
    expect(saveButton()).toBeDisabled()
    expect(
      screen.getByText('El outfit necesita calzado.'),
    ).toBeInTheDocument()
  })
})
