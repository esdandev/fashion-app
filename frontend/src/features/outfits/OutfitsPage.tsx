import { Link } from 'react-router-dom'

export default function OutfitsPage() {
  return (
    <section aria-labelledby="outfits-title" className="space-y-6">
      <div>
        <h1
          id="outfits-title"
          className="font-display text-3xl font-semibold tracking-tight text-stone-900 sm:text-4xl"
        >
          Outfits
        </h1>
        <p className="mt-1 text-sm text-stone-500">
          Combinaciones armadas con tus prendas reales.
        </p>
      </div>

      <div className="rounded-2xl border border-stone-200 bg-white p-6 shadow-sm sm:p-10">
        <p className="text-xs font-medium tracking-widest text-stone-400 uppercase">
          Próximamente
        </p>
        <h2 className="font-display mt-2 text-2xl font-semibold text-stone-900">
          Tus outfits se van a armar acá
        </h2>
        <p className="mt-2 max-w-prose text-sm text-stone-600">
          Vas a poder pedir sugerencias según ocasión y clima, editar las
          propuestas y guardar tus favoritos. Todo con las fotos de tu propio
          guardarropa.
        </p>
        <Link
          to="/guardarropa"
          className="mt-5 inline-flex rounded-xl bg-stone-900 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-stone-700"
        >
          Empezar por el guardarropa
        </Link>
      </div>
    </section>
  )
}
