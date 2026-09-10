/** Opt-in measurements for this Mock app only; no production runtime dependency. */
export default defineNuxtPlugin({
  name: 'mock-diagnostics',
  enforce: 'pre',
  setup(app) {
    const started = performance.now()
    performance.setResourceTimingBufferSize(5000)
    const components = new Map<string, { mounted: number; updated: number; unmounted: number }>()
    const count = (instance: any, event: 'mounted' | 'updated' | 'unmounted') => {
      const name = instance.$options.__name ?? instance.$options.name ?? 'anonymous'
      const record = components.get(name) ?? { mounted: 0, updated: 0, unmounted: 0 }
      record[event]++
      components.set(name, record)
    }
    app.vueApp.mixin({
      mounted() { count(this, 'mounted') },
      updated() { count(this, 'updated') },
      unmounted() { count(this, 'unmounted') },
    })
    let longTaskCount = 0
    let longTaskMilliseconds = 0
    let longestTask = 0
    const observer = new PerformanceObserver(list => {
      for (const entry of list.getEntries()) { longTaskCount++; longTaskMilliseconds += entry.duration; longestTask = Math.max(longestTask, entry.duration) }
    })
    if (PerformanceObserver.supportedEntryTypes.includes('longtask')) observer.observe({ type: 'longtask', buffered: true })
    const interval = setInterval(() => {
      const apiRequests: Record<string, number> = {}
      for (const entry of performance.getEntriesByType('resource')) {
        const path = new URL(entry.name).pathname
        if (path.startsWith('/api/') || path.startsWith('/hubs/')) apiRequests[path] = (apiRequests[path] ?? 0) + 1
      }
      document.documentElement.setAttribute('data-mock-diagnostics', JSON.stringify({
        elapsed: Math.round(performance.now() - started), path: location.pathname,
        longTaskCount, longTaskMilliseconds: Math.round(longTaskMilliseconds), longestTask: Math.round(longestTask),
        components: Object.fromEntries(components), apiRequests,
      }))
    }, 1000)
    if (import.meta.hot) import.meta.hot.dispose(() => { clearInterval(interval); observer.disconnect(); document.documentElement.removeAttribute('data-mock-diagnostics') })
  },
})
