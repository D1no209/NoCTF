import { toRefs } from 'vue'

import type { NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol } from '../../api'

/** Owns state, effects and commands for CompetitionCountdown. */
export function useCompetitionCountdown(props: Readonly<{
  startTime?: string
  endTime?: string
  status?: NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol
}>) {
  const now = ref(Date.now())

  let timer: ReturnType<typeof setInterval> | undefined

  onMounted(() => {
    timer = setInterval(() => {
      now.value = Date.now()
    }, 1000)
  })

  onUnmounted(() => {
    if (timer) clearInterval(timer)
  })

  const text = computed(() => {
    const start = props.startTime ? new Date(props.startTime).getTime() : null
    const end = props.endTime ? new Date(props.endTime).getTime() : null
    if (props.status === 'Finished') return translate("common.label.finished")
    if (start !== null && now.value < start) return translate("competitions.label.starts", { duration: formatDuration(start - now.value) })
    if (end !== null && now.value < end) return translate("competitions.label.ends", { duration: formatDuration(end - now.value) })
    if (end !== null) return translate("common.label.finished")
    return ''
  })

  return {
      ...toRefs(props),
      text
    }
}

export type CompetitionCountdownViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useCompetitionCountdown>>>
