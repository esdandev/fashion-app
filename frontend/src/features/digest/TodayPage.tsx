import { Link } from 'react-router-dom'

export default function TodayPage() {
  return (
    <section aria-labelledby="today-title" className="space-y-6">
      <div>
        <h1
          id="today-title"
          className="font-display text-3xl font-semibold tracking-tight text-stone-900 sm:text-4xl"
        >
          Hoy
        </h1>
        <p className="mt-1 text-sm text-stone-500">
          Lo que pasa en la moda, conectado con las prendas que ya tenés.
        </p>
      </div>

      <div className="rounded-2xl border border-stone-200 bg-white p-6 shadow-sm sm:p-10">
        <p className="text-xs font-medium tracking-widest text-stone-400 uppercase">
          Próximamente
        </p>
        <h2 className="font-display mt-2 text-2xl font-semibold text-stone-900">
          El resumen del día va a aparecer acá
        </h2>
        <p className="mt-2 max-w-prose text-sm text-stone-600">
          Cada mañana vas a encontrar 6–10 noticias y tendencias resumidas en
          español, con links a la fuente y las prendas de tu guardarropa que
          combinan con cada una.
        </p>
        <Link
          to="/guardarropa"
          className="mt-5 inline-flex rounded-xl bg-stone-900 px-4 py-2 text-sm font-medium text-white transition-colors hover:bg-stone-700"
        >
          Mientras tanto, ver mi guardarropa
        </Link>
      </div>
    </section>
  )
}
