import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, cleanup, render, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { api } from '../../lib/api'
import type { Garment, GarmentListResponse } from '../../lib/types'
import WardrobePage from './WardrobePage'

function garment(id: string, name: string, colorName = 'beige'): Garment {
  return {
    id,
    name,
    category: 'top',
    colorName,
    colorHex: '#d8c3a5',
    formality: 3,
    confidence: 0.9,
    seasons: ['spring'],
    styleTags: ['minimalista'],
    imageUrl: `/media/${id}.webp`,
    thumbUrl: `/media/${id}-thumb.webp`,
  }
}

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0 } },
  })

  return render(
    <QueryClientProvider client={queryClient}>
      <WardrobePage />
    </QueryClientProvider>,
  )
}

afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
})

describe('WardrobePage', () => {
  it('appends the next page when Cargar más is pressed', async () => {
    const first: GarmentListResponse = {
      items: [garment('1', 'Camisa de lino')],
      nextCursor: 'cursor-1',
    }
    const second: GarmentListResponse = {
      items: [garment('2', 'Pantalón beige')],
      nextCursor: null,
    }

    const getGarments = vi
      .spyOn(api, 'getGarments')
      .mockResolvedValueOnce(first)
      .mockResolvedValueOnce(second)
    vi.spyOn(api, 'getConfig').mockResolvedValue({ demo: false })

    renderPage()

    expect(await screen.findByText('Camisa de lino')).toBeInTheDocument()
    expect(screen.getByText('1 prenda cargada · hay más')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Cargar más' }))

    expect(await screen.findByText('Pantalón beige')).toBeInTheDocument()
    expect(screen.getByText('Camisa de lino')).toBeInTheDocument()
    expect(screen.getByText('2 prendas cargadas')).toBeInTheDocument()
    expect(getGarments).toHaveBeenLastCalledWith(
      '?cursor=cursor-1',
      expect.anything(),
    )
  })

  it('debounces the color filter and sends it to the API', async () => {
    const getGarments = vi
      .spyOn(api, 'getGarments')
      .mockResolvedValue({ items: [garment('1', 'Camisa de lino')], nextCursor: null })
    vi.spyOn(api, 'getConfig').mockResolvedValue({ demo: false })

    renderPage()

    expect(await screen.findByText('Camisa de lino')).toBeInTheDocument()

    fireEvent.change(screen.getByPlaceholderText('Todos'), {
      target: { value: 'beige' },
    })

    await waitFor(
      () =>
        expect(getGarments).toHaveBeenLastCalledWith(
          '?color=beige',
          expect.anything(),
        ),
      { timeout: 2000 },
    )
  })

  it('offers loaded colors in the filter datalist', async () => {
    vi.spyOn(api, 'getGarments').mockResolvedValue({
      items: [
        garment('1', 'Camisa', 'Beige'),
        garment('2', 'Abrigo', 'negro'),
        garment('3', 'Jersey', 'beige'),
      ],
      nextCursor: null,
    })
    vi.spyOn(api, 'getConfig').mockResolvedValue({ demo: false })

    const { container } = renderPage()

    expect(await screen.findByText('Camisa')).toBeInTheDocument()

    const options = Array.from(
      container.querySelectorAll('#wardrobe-color-options option'),
    ).map((option) => option.getAttribute('value'))
    expect(options).toHaveLength(2)
    expect(options).toEqual(expect.arrayContaining(['Beige', 'negro']))
  })
})
