import { toRefs } from 'vue'

import { Copy } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { isRuntimeUrlClickable } from '../../utils/runtime-url'

/** Owns state, effects and commands for RuntimeAccessUrl. */
export function useRuntimeAccessUrl(props: Readonly<{ url: string }>) {
  const clickable = computed(() => isRuntimeUrlClickable(props.url))

  async function copy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(props.url)
      toast.success(translate("ui.copiedToClipboard"))
    }
    catch {
      toast.error(translate("ui.copyFailedPleaseManuallySelectCopy"))
    }
  }

  return {
      ...toRefs(props),
      Copy,
      clickable,
      copy
    }
}

export type RuntimeAccessUrlViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useRuntimeAccessUrl>>>
