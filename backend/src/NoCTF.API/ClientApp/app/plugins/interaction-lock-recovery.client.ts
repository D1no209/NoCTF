import { installInteractionLockRecovery } from '~/features/shell/interaction-lock-recovery'

export default defineNuxtPlugin((app) => {
  const stop = installInteractionLockRecovery()
  app.vueApp.onUnmount(stop)
})
