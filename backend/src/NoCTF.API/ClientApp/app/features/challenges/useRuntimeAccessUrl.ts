import { message as describeMessage } from '../../utils/i18n'
import { toRefs } from 'vue'

import { Copy } from '@lucide/vue'
import { toast } from '../../utils/message-toast'
import { isRuntimeUrlClickable } from '../../utils/runtime-url'
import type { NoCtfapiEndpointsRuntimeRuntimeAccessResponse } from '../../api'

/** Owns state, effects and commands for RuntimeAccessUrl. */
export function useRuntimeAccessUrl(props: Readonly<{
  access: NoCtfapiEndpointsRuntimeRuntimeAccessResponse
}>) {
  const entries = computed(() => [
    props.access.directAddress
      ? {
          kind: 'direct' as const,
          address: props.access.directAddress,
          clickable: isRuntimeUrlClickable(props.access.directAddress),
        }
      : null,
    props.access.webSocketAddress
      ? {
          kind: 'wsrx' as const,
          address: props.access.webSocketAddress,
          clickable: false,
        }
      : null,
  ].filter(entry => entry !== null))

  async function copy(address: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(address)
      toast.success(describeMessage("common.label.copiedClipboard"))
    }
    catch {
      toast.error(describeMessage("common.kohPanel.error.copyManuallySelectFailed"))
    }
  }

  return {
      ...toRefs(props),
      Copy,
      entries,
      copy
    }
}

export type RuntimeAccessUrlViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useRuntimeAccessUrl>>>
