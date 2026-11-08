import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { api } from '../../lib/api'
import type { Garment, Outfit } from '../../lib/types'
import { OutfitEditor } from './OutfitEditor'

function garment(
  id: string,
  name: string,
  category: Garment['category'],
  formality = 3,
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

const camisa = garment('camisa', 'Camisa de lino', 'top')
const jean = garment('jean', 'Jean recto', 'bottom')
const zapatillas = garment('zapatillas', 'Zapatillas blancas', 'shoes', 2)
const archivedShoes = garment('archivadas', 'Zapatillas viejas', 'shoes', 2)

function outfitFixture(garments: Garment[]): Outfit {
  return {
    id: 'outfit-1',
    name: 'Look de trabajo',
    occasion: 'trabajo',
    source: 'manual',
    rationale: null,
    isFavorite: false,
    createdAt: '2026-01-01T00:00:00Z',
    items: garments.map((garment, index) => ({
      garment,
      layer: 0,
      sortOrder: index,
    })),
  }
}

function renderEditor(outfit?: Outfit) {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false, gcTime: 0 },
      mutations: { retry: false },
    },
  })
  const invalidate = vi.spyOn(queryClient, 'invalidateQueries')
  const onClose = vi.fn()

  render(
    <QueryClientProvider client={queryClient}>
      <OutfitEditor outfit={outfit} onClose={onClose} />
    </QueryClientProvider>,
  )

  return { onClose, invalidate }
}

function saveButton() {
  return screen.getByRole('button', { name: /guardar outfit/i })
}

afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
})

describe('OutfitEditor', () => {
  it('creates the outfit with POST and returns to the list', async () => {
    const user = userEvent.setup()
    const saved = outfitFixture([camisa, jean, zapatillas])
    const create = vi.spyOn(api, 'createOutfit').mockResolvedValue(saved)
    const patch = vi.spyOn(api, 'patchOutfit').mockResolvedValue(saved)
    vi.spyOn(api, 'getAllGarments').mockResolvedValue([
      camisa,
      jean,
      zapatillas,
    ])
    const { onClose, invalidate } = renderEditor()

    expect(
      await screen.findByRole('checkbox', { name: /camisa/i }),
    ).toBeInTheDocument()

    await user.type(screen.getByLabelText('Nombre'), 'Look casual')
    await user.click(screen.getByRole('checkbox', { name: /camisa/i }))
    await user.click(screen.getByRole('checkbox', { name: /jean/i }))
    await user.click(screen.getByRole('checkbox', { name: /zapatillas/i }))
    await user.click(saveButton())

    await waitFor(() =>
      expect(create).toHaveBeenCalledWith({
        name: 'Look casual',
        occasion: null,
        garmentIds: ['camisa', 'jean', 'zapatillas'],
      }),
    )
    await waitFor(() => expect(onClose).toHaveBeenCalled())
    expect(patch).not.toHaveBeenCalled()
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['outfits'] })
  })

  it('prefills the outfit and PATCHes metadata without garmentIds when unchanged', async () => {
    const user = userEvent.setup()
    const current = outfitFixture([camisa, jean, archivedShoes])
    const patch = vi.spyOn(api, 'patchOutfit').mockResolvedValue(current)
    const create = vi.spyOn(api, 'createOutfit').mockResolvedValue(current)
    // Archived shoes are no longer in the wardrobe: they can only come from
    // the outfit being edited.
    vi.spyOn(api, 'getAllGarments').mockResolvedValue([
      camisa,
      jean,
      zapatillas,
    ])
    const { onClose, invalidate } = renderEditor(current)

    expect(
      await screen.findByRole('checkbox', { name: /camisa/i }),
    ).toBeChecked()
    expect(screen.getByLabelText('Nombre')).toHaveValue('Look de trabajo')
    expect(screen.getByLabelText('Ocasión')).toHaveValue('trabajo')
    expect(screen.getByText('Zapatillas viejas')).toBeInTheDocument()

    const nameInput = screen.getByLabelText('Nombre')
    await user.clear(nameInput)
    await user.type(nameInput, 'Look renovado')
    await user.click(saveButton())

    await waitFor(() =>
      expect(patch).toHaveBeenCalledWith('outfit-1', {
        name: 'Look renovado',
        occasion: 'trabajo',
      }),
    )
    expect(patch.mock.calls[0][1]).not.toHaveProperty('garmentIds')
    expect(create).not.toHaveBeenCalled()
    await waitFor(() => expect(onClose).toHaveBeenCalled())
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['outfits'] })
  })

  it('PATCHes only active garment ids once the selection changes', async () => {
    const user = userEvent.setup()
    const current = outfitFixture([camisa, jean, archivedShoes])
    const patch = vi.spyOn(api, 'patchOutfit').mockResolvedValue(current)
    vi.spyOn(api, 'getAllGarments').mockResolvedValue([
      camisa,
      jean,
      zapatillas,
    ])
    const { onClose, invalidate } = renderEditor(current)

    expect(
      await screen.findByRole('checkbox', { name: /camisa/i }),
    ).toBeChecked()

    await user.click(screen.getByRole('checkbox', { name: /zapatillas/i }))
    await user.click(saveButton())

    await waitFor(() =>
      expect(patch).toHaveBeenCalledWith('outfit-1', {
        name: 'Look de trabajo',
        occasion: 'trabajo',
        garmentIds: ['camisa', 'jean', 'zapatillas'],
      }),
    )
    expect(patch.mock.calls[0][1].garmentIds).not.toContain('archivadas')
    await waitFor(() => expect(onClose).toHaveBeenCalled())
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['outfits'] })
  })
})
