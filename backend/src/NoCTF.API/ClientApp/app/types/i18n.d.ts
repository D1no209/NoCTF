import type { translate } from '~/utils/i18n'

declare module 'vue' {
  interface ComponentCustomProperties {
    $t: typeof translate
  }
}

export {}
