import { markRaw, toRefs } from 'vue'

import { toast } from 'vue-sonner'
import { Copy } from '@lucide/vue'
import type { NoCtfapiEndpointsChallengesChallengeResponse, NoCtfapiEndpointsCompetitionsCompetitionResponse } from '../../../api'
import RuntimeAccessUrlComponent from '../RuntimeAccessUrl.vue'

/** Owns state, effects and commands for KohPanel. */
export function useKohPanel(props: Readonly<{
  competition: NoCtfapiEndpointsCompetitionsCompetitionResponse
  challenge: NoCtfapiEndpointsChallengesChallengeResponse
}>) {
  async function copyControlFlag(flag: string) {
    try {
      await navigator.clipboard.writeText(flag)
      toast.success(translate("ui.controlFlagCopied"))
    }
    catch {
      toast.error(translate("ui.copyFailedPleaseManuallySelectCopy"))
    }
  }

  const RuntimeAccessUrl = markRaw(RuntimeAccessUrlComponent)

  return {
      ...toRefs(props),
      Copy,
      copyControlFlag,
      RuntimeAccessUrl
    }
}

export type KohPanelViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useKohPanel>>>
