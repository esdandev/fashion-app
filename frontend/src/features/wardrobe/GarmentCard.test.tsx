import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { cleanup, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { api } from '../../lib/api'
import type { Garment } from '../../lib/types'
import { GarmentCard } from './GarmentCard'

vi.mock('../../lib/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../lib/api')>()
  return {
    ...actual,
    api: {
      ...actual.api,
      deleteGarment: vi.fn().mockResolvedValue(undefined),
    },
  }
})

const camisa: Garment = {
  id: 'camisa',
  name: 'Camisa de lino',
  category: 'top',
  colorName: 'beige',
  colorHex: '#d8c3a5',
  formality: 3,
  confidence: 0.9,
  seasons: ['spring'],
  styleTags: ['minimalista'],
  imageUrl: '/media/camisa.webp',
  thumbUrl: '/media/camisa-thumb.webp',
}

function renderCard(readOnly = false) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })

  return render(
    <QueryClientProvider client={queryClient}>
      <GarmentCard garment={camisa} readOnly={readOnly} />
    </QueryClientProvider>,
  )
}

afterEach(() => {
  cleanup()
  vi.clearAllMocks()
})

describe('GarmentCard', () => {
  it('asks for confirmation and deletes the garment', async () => {
    const user = userEvent.setup()
    renderCard()

    await user.click(screen.getByRole('button', { name: 'Eliminar prenda' }))
    expect(screen.getByText(/¿Eliminar «Camisa de lino»/)).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Sí, eliminar' }))

    await waitFor(() => {
      expect(api.deleteGarment).toHaveBeenCalledWith('camisa')
    })
  })

  it('does not offer deletion in demo mode', () => {
    renderCard(true)

    expect(
      screen.queryByRole('button', { name: 'Eliminar prenda' }),
    ).not.toBeInTheDocument()
  })
})
