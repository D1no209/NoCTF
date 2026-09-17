import { topNavMotionDirective } from '~/motion/top-nav-width'

export default defineNuxtPlugin((app) => {
  app.vueApp.directive('top-nav-motion', topNavMotionDirective)
})
