import type { translate, localizeMessage } from '../utils/i18n'

declare module 'vue' {
  interface ComponentCustomProperties {
    $t: typeof translate
    $message: typeof localizeMessage
  }
}

export {}
