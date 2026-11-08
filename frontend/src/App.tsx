import { Suspense, lazy } from 'react'
import { Link, NavLink, Route, Routes } from 'react-router-dom'

const TodayPage = lazy(() => import('./features/digest/TodayPage'))
const WardrobePage = lazy(() => import('./features/wardrobe/WardrobePage'))
const OutfitsPage = lazy(() => import('./features/outfits/OutfitsPage'))

function navLinkClass({ isActive }: { isActive: boolean }): string {
  return [
    'rounded-full px-3 py-1.5 text-sm font-medium transition-colors',
    isActive
      ? 'bg-stone-900 text-white'
      : 'text-stone-600 hover:bg-stone-200 hover:text-stone-900',
  ].join(' ')
}

function RouteFallback() {
  return (
    <div
      role="status"
      className="rounded-2xl border border-stone-200 bg-white p-10 text-center text-sm text-stone-500"
    >
      Cargando…
    </div>
  )
}

function NotFound() {
  return (
    <div className="rounded-2xl border border-stone-200 bg-white p-10 text-center">
      <h1 className="font-display text-2xl font-semibold text-stone-900">
        Página no encontrada
      </h1>
      <p className="mt-2 text-sm text-stone-500">
        El link que abriste no existe.
      </p>
      <Link
        to="/"
        className="mt-4 inline-flex rounded-xl bg-stone-900 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-stone-700"
      >
        Volver al inicio
      </Link>
    </div>
  )
}

export default function App() {
  return (
    <div className="flex min-h-svh flex-col">
      <header className="sticky top-0 z-10 border-b border-stone-200/80 bg-stone-50/90 backdrop-blur">
        <div className="mx-auto flex w-full max-w-6xl flex-wrap items-center justify-between gap-3 px-4 py-3 sm:px-6">
          <Link
            to="/"
            className="font-display text-xl font-semibold tracking-tight text-stone-900"
          >
            Fashion App
          </Link>
          <nav aria-label="Secciones principales">
            <ul className="flex items-center gap-1">
              <li>
                <NavLink to="/" end className={navLinkClass}>
                  Hoy
                </NavLink>
              </li>
              <li>
                <NavLink to="/guardarropa" className={navLinkClass}>
                  Guardarropa
                </NavLink>
              </li>
              <li>
                <NavLink to="/outfits" className={navLinkClass}>
                  Outfits
                </NavLink>
              </li>
            </ul>
          </nav>
        </div>
      </header>

      <main className="mx-auto w-full max-w-6xl flex-1 px-4 py-6 sm:px-6 sm:py-10">
        <Suspense fallback={<RouteFallback />}>
          <Routes>
            <Route path="/" element={<TodayPage />} />
            <Route path="/guardarropa" element={<WardrobePage />} />
            <Route path="/outfits" element={<OutfitsPage />} />
            <Route path="*" element={<NotFound />} />
          </Routes>
        </Suspense>
      </main>

      <footer className="border-t border-stone-200">
        <div className="mx-auto flex w-full max-w-6xl flex-wrap items-center justify-between gap-2 px-4 py-4 text-xs text-stone-400 sm:px-6">
          <p>Fashion App · proyecto personal</p>
          <p>Guardarropa privado, sin cuentas.</p>
        </div>
      </footer>
    </div>
  )
}
