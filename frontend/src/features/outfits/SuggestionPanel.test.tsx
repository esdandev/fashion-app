import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, api } from '../../lib/api'
import type { Garment, Outfit } from '../../lib/types'
import { SuggestionPanel } from './SuggestionPanel'

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
const zapatillas = garment('zapatillas', 'Zapatillas blancas', 'shoes')

const savedOutfit: Outfit = {
  id: 'outfit-1',
  name: 'Propuesta de dos piezas',
  occasion: 'trabajo',
  source: 'ai',
  rationale: 'Ideal para trabajar con clima templado.',
  isFavorite: false,
  createdAt: '2026-01-01T00:00:00Z',
  items: [camisa, jean, zapatillas].map((item, index) => ({
    garment: item,
    layer: 0,
    sortOrder: index,
  })),
}

function renderPanel() {
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
      <SuggestionPanel onClose={onClose} />
    </QueryClientProvider>,
  )

  return { invalidate, onClose }
}

afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
})

describe('SuggestionPanel', () => {
  it('generates proposals without persisting and saves one with source=ai', async () => {
    const user = userEvent.setup()
    vi.spyOn(api, 'getAllGarments').mockResolvedValue([
      camisa,
      jean,
      zapatillas,
    ])
    const suggest = vi.spyOn(api, 'suggestOutfits').mockResolvedValue({
      outfits: [
        {
          name: 'Propuesta de dos piezas',
          garmentIds: ['camisa', 'jean', 'zapatillas'],
          occasion: 'trabajo',
          rationale: 'Ideal para trabajar con clima templado.',
        },
      ],
    })
    const create = vi.spyOn(api, 'createOutfit').mockResolvedValue(savedOutfit)
    const { invalidate } = renderPanel()

    const generate = await screen.findByRole('button', { name: 'Generar' })
    await waitFor(() => expect(generate).toBeEnabled())

    await user.selectOptions(screen.getByLabelText('Ocasión'), 'trabajo')
    await user.selectOptions(screen.getByLabelText('Clima'), 'templado')
    await user.click(generate)

    expect(await screen.findByText('Outfit sugerido')).toBeInTheDocument()
    expect(
      screen.getByText('Ideal para trabajar con clima templado.'),
    ).toBeInTheDocument()
    expect(create).not.toHaveBeenCalled()
    expect(suggest).toHaveBeenCalledWith({
      occasion: 'trabajo',
      weather: 'templado',
    })

    await user.click(screen.getByRole('button', { name: 'Guardar' }))

    await waitFor(() =>
      expect(create).toHaveBeenCalledWith({
        name: 'Propuesta de dos piezas',
        occasion: 'trabajo',
        garmentIds: ['camisa', 'jean', 'zapatillas'],
        source: 'ai',
        rationale: 'Ideal para trabajar con clima templado.',
      }),
    )
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['outfits'] })
    expect(
      await screen.findByText('Outfit guardado en tu lista.'),
    ).toBeInTheDocument()
  })

  it('disables generation and explains when the wardrobe cannot form an outfit', async () => {
    vi.spyOn(api, 'getAllGarments').mockResolvedValue([camisa, jean])
    renderPanel()

    const generate = await screen.findByRole('button', { name: 'Generar' })
    await waitFor(() => expect(generate).toBeDisabled())
    expect(
      screen.getByText(
        /necesitás al menos una parte superior y una inferior, o un vestido, más calzado/i,
      ),
    ).toBeInTheDocument()
  })

  it('shows the exhausted quota and the Retry-After in minutes', async () => {
    const user = userEvent.setup()
    vi.spyOn(api, 'getAllGarments').mockResolvedValue([
      camisa,
      jean,
      zapatillas,
    ])
    vi.spyOn(api, 'suggestOutfits').mockRejectedValue(
      new ApiError(
        'Se alcanzó el límite de sugerencias de IA para hoy (UTC).',
        429,
        undefined,
        3600,
      ),
    )
    renderPanel()

    const generate = await screen.findByRole('button', { name: 'Generar' })
    await waitFor(() => expect(generate).toBeEnabled())
    await user.click(generate)

    expect(
      await screen.findByText(
        /demasiadas solicitudes.*60 minutos/i,
      ),
    ).toBeInTheDocument()
  })

  it('falls back to a clear quota message when Retry-After is absent', async () => {
    const user = userEvent.setup()
    vi.spyOn(api, 'getAllGarments').mockResolvedValue([
      camisa,
      jean,
      zapatillas,
    ])
    vi.spyOn(api, 'suggestOutfits').mockRejectedValue(
      new ApiError(
        'Se alcanzó el límite de sugerencias de IA para hoy (UTC).',
        429,
      ),
    )
    renderPanel()

    const generate = await screen.findByRole('button', { name: 'Generar' })
    await waitFor(() => expect(generate).toBeEnabled())
    await user.click(generate)

    expect(
      await screen.findByText(
        /demasiadas solicitudes.*en un rato/i,
      ),
    ).toBeInTheDocument()
    expect(
      screen.queryByText(/Ai:DailySuggestionLimit/i),
    ).not.toBeInTheDocument()
  })

  it('shows the empty state when the API returns no proposals', async () => {
    const user = userEvent.setup()
    vi.spyOn(api, 'getAllGarments').mockResolvedValue([
      camisa,
      jean,
      zapatillas,
    ])
    vi.spyOn(api, 'suggestOutfits').mockResolvedValue({ outfits: [] })
    renderPanel()

    const generate = await screen.findByRole('button', { name: 'Generar' })
    await waitFor(() => expect(generate).toBeEnabled())
    await user.click(generate)

    expect(
      await screen.findByText(
        /No encontramos combinaciones para esa ocasión y clima/,
      ),
    ).toBeInTheDocument()
  })

  it('shows a generic suggest error and retries the request', async () => {
    const user = userEvent.setup()
    vi.spyOn(api, 'getAllGarments').mockResolvedValue([
      camisa,
      jean,
      zapatillas,
    ])
    const suggest = vi
      .spyOn(api, 'suggestOutfits')
      .mockRejectedValueOnce(
        new ApiError('El servicio de sugerencias no está disponible.', 500),
      )
      .mockResolvedValueOnce({
        outfits: [
          {
            name: 'Propuesta recuperada',
            garmentIds: ['camisa', 'jean', 'zapatillas'],
            occasion: null,
            rationale: 'La segunda llamada funcionó.',
          },
        ],
      })
    renderPanel()

    const generate = await screen.findByRole('button', { name: 'Generar' })
    await waitFor(() => expect(generate).toBeEnabled())
    await user.click(generate)

    expect(
      await screen.findByText('No pudimos generar sugerencias.'),
    ).toBeInTheDocument()
    expect(
      screen.getByText('El servicio de sugerencias no está disponible.'),
    ).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Reintentar' }))

    await waitFor(() => expect(suggest).toHaveBeenCalledTimes(2))
    expect(await screen.findByText('Propuesta recuperada')).toBeInTheDocument()
  })

  it('shows a save failure without losing the proposal', async () => {
    const user = userEvent.setup()
    vi.spyOn(api, 'getAllGarments').mockResolvedValue([
      camisa,
      jean,
      zapatillas,
    ])
    vi.spyOn(api, 'suggestOutfits').mockResolvedValue({
      outfits: [
        {
          name: 'Propuesta de dos piezas',
          garmentIds: ['camisa', 'jean', 'zapatillas'],
          occasion: 'trabajo',
          rationale: 'Ideal para trabajar con clima templado.',
        },
      ],
    })
    vi.spyOn(api, 'createOutfit').mockRejectedValue(
      new ApiError('No pudimos guardar el outfit en este momento.', 500),
    )
    renderPanel()

    const generate = await screen.findByRole('button', { name: 'Generar' })
    await waitFor(() => expect(generate).toBeEnabled())
    await user.click(generate)
    await screen.findByText('Outfit sugerido')

    await user.click(screen.getByRole('button', { name: 'Guardar' }))

    expect(
      await screen.findByText('No pudimos guardar el outfit en este momento.'),
    ).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Guardar' })).toBeEnabled()
  })

  it('clears a previous save error when a new suggestion succeeds', async () => {
    const user = userEvent.setup()
    vi.spyOn(api, 'getAllGarments').mockResolvedValue([
      camisa,
      jean,
      zapatillas,
    ])
    vi.spyOn(api, 'suggestOutfits')
      .mockResolvedValueOnce({
        outfits: [
          {
            name: 'Primera propuesta',
            garmentIds: ['camisa', 'jean', 'zapatillas'],
            occasion: 'trabajo',
            rationale: 'Primera tanda.',
          },
        ],
      })
      .mockResolvedValueOnce({
        outfits: [
          {
            name: 'Segunda propuesta',
            garmentIds: ['camisa', 'jean', 'zapatillas'],
            occasion: 'cena',
            rationale: 'Segunda tanda.',
          },
        ],
      })
    vi.spyOn(api, 'createOutfit').mockRejectedValue(
      new ApiError('El guardado falló en la primera tanda.', 500),
    )
    renderPanel()

    const generate = await screen.findByRole('button', { name: 'Generar' })
    await waitFor(() => expect(generate).toBeEnabled())
    await user.click(generate)
    await screen.findByText('Primera propuesta')

    await user.click(screen.getByRole('button', { name: 'Guardar' }))
    expect(
      await screen.findByText('El guardado falló en la primera tanda.'),
    ).toBeInTheDocument()

    await user.selectOptions(screen.getByLabelText('Ocasión'), 'cena')
    await user.click(generate)

    expect(await screen.findByText('Segunda propuesta')).toBeInTheDocument()
    expect(
      screen.queryByText('El guardado falló en la primera tanda.'),
    ).not.toBeInTheDocument()
  })
})
