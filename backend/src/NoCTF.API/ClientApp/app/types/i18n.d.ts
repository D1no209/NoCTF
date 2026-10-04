import type { t, localizeMessage } from '../utils/i18n'

declare module 'vue' {
  interface ComponentCustomProperties {
    $t: typeof t
    $message: typeof localizeMessage
  }
}

export {}
