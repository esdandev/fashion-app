import { Suspense, lazy } from 'react'
import { Link, NavLink, Route, Routes } from 'react-router-dom'

const TodayPage = lazy(() => import('./features/digest/TodayPage'))
const WardrobePage = lazy(() => import('./features/wardrobe/WardrobePage'))
const OutfitsPage = lazy(() => import('./features/outfits/OutfitsPage'))

function navLinkClass({ isActive }: { isActive: boolean }): string {
  return [
    'border-b pb-0.5 font-sans text-[11px] font-medium tracking-[0.16em] uppercase transition-colors',
    isActive
      ? 'border-oxblood text-oxblood'
      : 'border-transparent text-stone-500 hover:border-stone-400 hover:text-ink',
  ].join(' ')
}

function RouteFallback() {
  return (
    <div
      role="status"
      className="editorial-surface px-6 py-12 text-center font-sans text-sm text-stone-500"
    >
      Cargando…
    </div>
  )
}

function NotFound() {
  return (
    <div className="editorial-surface px-6 py-16 text-center">
      <p className="editorial-kicker">Error 404</p>
      <h1 className="editorial-section-title mt-3">Página no encontrada</h1>
      <p className="mx-auto mt-4 max-w-sm font-serif text-sm text-stone-600">
        El link que abriste no existe.
      </p>
      <Link to="/" className="editorial-btn mt-6">
        Volver al inicio
      </Link>
    </div>
  )
}

export default function App() {
  return (
    <div className="flex min-h-svh flex-col">
      <header className="sticky top-0 z-10 border-b border-stone-300/80 bg-paper/92 backdrop-blur">
        <div className="mx-auto flex w-full max-w-[1320px] flex-wrap items-end justify-between gap-x-6 gap-y-3 px-4 py-3 sm:px-8">
          <Link
            to="/"
            className="font-display text-2xl leading-none tracking-[-0.03em] text-ink sm:text-[28px]"
          >
            Fashion App<span className="text-oxblood">.</span>
          </Link>
          <nav aria-label="Secciones principales">
            <ul className="flex items-center gap-5 sm:gap-7">
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
          <p className="hidden text-right font-sans text-[10px] leading-relaxed tracking-[0.12em] text-stone-500 uppercase lg:block">
            Edición diaria
            <br />
            Buenos Aires
          </p>
        </div>
      </header>

      <main className="mx-auto w-full max-w-[1320px] flex-1 px-4 py-8 sm:px-8 sm:py-10">
        <Suspense fallback={<RouteFallback />}>
          <Routes>
            <Route path="/" element={<TodayPage />} />
            <Route path="/guardarropa" element={<WardrobePage />} />
            <Route path="/outfits" element={<OutfitsPage />} />
            <Route path="*" element={<NotFound />} />
          </Routes>
        </Suspense>
      </main>

      <footer className="border-t-[3px] border-double border-ink/70">
        <div className="mx-auto flex w-full max-w-[1320px] flex-wrap items-center justify-between gap-2 px-4 py-4 font-sans text-[10px] tracking-[0.12em] text-stone-500 uppercase sm:px-8">
          <p>Fashion App · proyecto personal</p>
          <p>Guardarropa privado, sin cuentas.</p>
        </div>
      </footer>
    </div>
  )
}
