import { message as describeMessage } from '../../../utils/i18n'
import { markRaw, toRefs } from 'vue'

import { toast } from '../../../utils/message-toast'
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
      toast.success(describeMessage("challenges.label.controlFlagCopied"))
    }
    catch {
      toast.error(describeMessage("common.kohPanel.error.copyManuallySelectFailed"))
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
