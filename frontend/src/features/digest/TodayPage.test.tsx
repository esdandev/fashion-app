import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError, api } from '../../lib/api'
import type { DigestDto, DigestItemDto } from '../../lib/types'
import TodayPage from './TodayPage'

function item(overrides: Partial<DigestItemDto> = {}): DigestItemDto {
  return {
    id: 'item-1',
    title: 'El regreso del denim',
    url: 'https://example.com/denim',
    source: 'Vogue',
    summary: 'El jean vuelve con cortes rectos y lavados claros.',
    styleTags: ['casual', 'denim'],
    imageUrl: '/media/news.webp',
    publishedAt: '2026-11-08T09:00:00Z',
    relatedGarments: [
      {
        id: 'garment-1',
        name: 'Jean recto',
        category: 'bottom',
        colorName: 'azul',
        colorHex: '#3b5b92',
        formality: 2,
        confidence: 0.9,
        seasons: ['autumn'],
        styleTags: ['casual'],
        imageUrl: '/media/garment.webp',
        thumbUrl: '/media/garment-thumb.webp',
      },
    ],
    ...overrides,
  }
}

function digest(overrides: Partial<DigestDto> = {}): DigestDto {
  return {
    date: '2026-11-08',
    summary: 'Un domingo de moda: denim, sastrería suave y colores tierra.',
    generatedAt: '2026-11-08T10:00:00Z',
    items: [item()],
    ...overrides,
  }
}

function renderPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0 } },
  })

  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <TodayPage />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

afterEach(() => {
  cleanup()
  vi.restoreAllMocks()
})

describe('TodayPage', () => {
  it('renders the hero, source link, tags and related garments', async () => {
    vi.spyOn(api, 'getDigest').mockResolvedValue(digest())

    renderPage()

    expect(
      await screen.findByText('domingo 8 de noviembre'),
    ).toBeInTheDocument()
    expect(
      screen.getByText(
        'Un domingo de moda: denim, sastrería suave y colores tierra.',
      ),
    ).toBeInTheDocument()
    expect(screen.getByText('1 novedad')).toBeInTheDocument()

    const link = screen.getByRole('link', {
      name: /El regreso del denim/,
    })
    expect(link).toHaveAttribute('href', 'https://example.com/denim')
    expect(link).toHaveAttribute('target', '_blank')
    expect(link).toHaveAttribute('rel', 'noopener noreferrer')

    expect(screen.getByText('Vogue')).toBeInTheDocument()
    expect(screen.getByText('denim')).toBeInTheDocument()
    expect(screen.getByAltText('Jean recto')).toHaveAttribute(
      'loading',
      'lazy',
    )
  })

  it('hides the related garments section when there are none', async () => {
    vi.spyOn(api, 'getDigest').mockResolvedValue(
      digest({ items: [item({ relatedGarments: [] })] }),
    )

    renderPage()

    expect(await screen.findByText('El regreso del denim')).toBeInTheDocument()
    expect(screen.queryByText('En tu guardarropa')).not.toBeInTheDocument()
  })

  it('treats a 404 as the empty state', async () => {
    vi.spyOn(api, 'getDigest').mockRejectedValue(
      new ApiError('No hay digest.', 404),
    )

    renderPage()

    expect(
      await screen.findByText('Todavía no hay resumen de hoy'),
    ).toBeInTheDocument()
    expect(
      screen.queryByText('No pudimos cargar el resumen de hoy.'),
    ).not.toBeInTheDocument()
  })

  it('shows the error state with a retry button', async () => {
    vi.spyOn(api, 'getDigest').mockRejectedValue(
      new ApiError('El servidor no responde.', 500),
    )

    renderPage()

    expect(
      await screen.findByText('No pudimos cargar el resumen de hoy.'),
    ).toBeInTheDocument()
    expect(
      screen.getByRole('button', { name: 'Reintentar' }),
    ).toBeInTheDocument()
  })
})
