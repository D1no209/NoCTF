import { defineComponent, h } from 'vue'
import { toast as sonner } from 'vue-sonner'
import type { UiMessage } from './i18n'
import { localizeMessage } from './i18n'

function content(message: UiMessage) {
  return typeof message === 'string' ? message : defineComponent({
    setup: () => () => h('span', localizeMessage(message)),
  })
}

/** Sonner keeps a rendering component, so visible feedback changes with the language. */
export const toast = {
  ...sonner,
  success: (message: UiMessage, options?: Parameters<typeof sonner.success>[1]) => sonner.success(content(message), options),
  error: (message: UiMessage, options?: Parameters<typeof sonner.error>[1]) => sonner.error(content(message), options),
  info: (message: UiMessage, options?: Parameters<typeof sonner.info>[1]) => sonner.info(content(message), options),
  warning: (message: UiMessage, options?: Parameters<typeof sonner.warning>[1]) => sonner.warning(content(message), options),
}
