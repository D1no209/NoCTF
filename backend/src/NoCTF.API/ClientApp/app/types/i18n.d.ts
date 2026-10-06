import type { t, localizeMessage } from '../utils/i18n'
import type { UnwrapRef } from 'vue'

// Nuxt declares template imports on `vue`; Vue's public instance uses runtime-core.
type TemplateImports = {
  readonly [Name in keyof typeof import('#imports')]: UnwrapRef<typeof import('#imports')[Name]>
}

declare module '@vue/runtime-core' {
  interface ComponentCustomProperties extends TemplateImports {
    $t: typeof t
    $message: typeof localizeMessage
  }
}

export {}
