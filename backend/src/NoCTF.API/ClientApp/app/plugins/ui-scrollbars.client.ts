import { installScrollbars } from '~/components/ui/scroll-area/scrollbars'

export default defineNuxtPlugin((app) => {
  let dispose: (() => void) | undefined
  app.hook('app:mounted', () => { dispose = installScrollbars() })
  if (import.meta.hot) import.meta.hot.dispose(() => dispose?.())
})
