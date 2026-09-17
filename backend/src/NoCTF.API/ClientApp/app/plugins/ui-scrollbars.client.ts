import { scrollSurfaceDirective } from '~/components/ui/scroll-area/scrollbars'

export default defineNuxtPlugin((app) => {
  app.vueApp.directive('scroll-surface', scrollSurfaceDirective)
})
