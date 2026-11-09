# Seguimiento de performance — hero de Hoy (`perf-1`)

**Estado:** **aceptada como deuda documentada para la Fase 4** (decisión del
controller). No se implementó en esta ola: se acepta el LCP medido de 3,21 s
contra el presupuesto de 2,5 s y el trabajo queda agendado en este documento.
No se hizo una reescritura riesgosa de performance en la Fase 4.

## Aceptación de la Fase 4

El presupuesto de LCP **no se cumple** en la medición local (3,14–3,22 s,
mediana 3,21 s). La Fase 4 se cierra con esa deuda **explícitamente aceptada**:

- el resto del presupuesto se cumple (CLS 0,060; TBT 42–55 ms como proxy de
  laboratorio de INP);
- el elemento LCP y el *breakdown* están medidos y versionados en
  [metrics/](metrics/README.md), así que la tarea de seguimiento es accionable;
- se prioriza cerrar deploy, demo, CI y documentación sin un cambio de
  arquitectura del hero que no se pueda medir en esta ola;
- el límite se repite en el [README](../README.md#límites-conocidos) y en la
  bitácora del spec.

Esta aceptación no es un "verde": es una deuda conocida con criterio de salida
y medición antes/después.

## Contexto

La medición de Lighthouse móvil sobre el build de producción local dejó el
**LCP en 3,14–3,22 s** (mediana 3,21 s) contra un presupuesto de 2,5 s. El
detalle, el comando y los JSON crudos están en
[metrics/README.md](metrics/README.md).

Los tres JSON identifican el subtítulo de la portada como elemento LCP:

```
main.mx-auto > section.space-y-6 > div > p.mt-1
Lo que pasa en la moda, conectado con las prendas que ya tenés.
```

El FCP es 0,90–0,96 s y el CLS queda en 0,060. El *breakdown* observado
(TTFB 9–21 ms, render delay 433–460 ms) no alcanza para explicar el LCP
simulado; por eso la causa sigue siendo una hipótesis.

## Objetivo

Bajar el LCP p75 móvil local a **≤ 2,5 s** sin degradar CLS (≤ 0,1), INP/TBT ni
el resto de la portada, y sin tocar los feeds.

## Hipótesis a validar

1. **Render del hero después del fetch.** La fecha, el resumen y el contador
   dependen de `GET /api/digest/today`; hasta que responde, la pantalla muestra
   el skeleton. Bajo throttling móvil, el trabajo de parseo/ejecución del bundle
   puede retrasar el primer render útil.
2. **Chunk bajo demanda.** `TodayPage` es un chunk lazy; el costo de descargar y
   evaluar el chunk + el cliente API puede entrar en la ruta crítica.
3. **Skeleton → contenido.** La altura del skeleton y del hero cargado puede no
   coincidir; se debe medir si hay shift o simplemente trabajo de layout.
4. **Fuente display.** La carga de la fuente del título puede influir en el
   primer render del bloque grande.

## Alcance propuesto

- Instrumentar la portada con `PerformanceObserver`/marks para separar
  `fetchStart`, respuesta del digest, primer render del hero y LCP real.
- Evaluar un *prefetch* del digest en paralelo con la carga del chunk (por
  ejemplo, en el arranque del router o con una query inicial) sin bloquear el
  render del shell.
- Evaluar un hero con altura estable y, si hace falta, un placeholder con la
  fecha del cliente para que el layout no dependa del fetch.
- Medir antes/después con el **mismo** comando y entorno de
  [metrics/README.md](metrics/README.md); 3 corridas y mediana.

## Criterios de aceptación

- LCP mediano local ≤ 2,5 s en 3 corridas con el build de producción y la demo
  sembrada.
- CLS ≤ 0,1 y TBT sin regresión significativa.
- Los E2E de Hoy/guardarropa/outfits siguen pasando.
- La captura de portfolio de Hoy sigue mostrando la tira «En tu guardarropa» y
  no se rompe el estado vacío ni el error del digest.
- Se actualizan `metrics/` y la tabla de Rendimiento del README con números
  nuevos, sin borrar los JSON de la medición anterior.

## Fuera de alcance

- Cambiar los feeds reales ni el motor de outfits.
- Introducir SSR o un framework distinto: primero medir si el prefetch y la
  estabilización del hero alcanzan.
- Optimizar el LCP de la instancia personal con datos reales; esta tarea mide
  la demo.
