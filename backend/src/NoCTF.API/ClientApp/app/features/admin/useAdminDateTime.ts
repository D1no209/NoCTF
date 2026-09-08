import { toRefs } from 'vue'

/** Owns state, effects and commands for AdminDateTime. */
export function useAdminDateTime(props: Readonly<{ value?: string | null }>) {
  const formatted = computed(() => {
    if (!props.value) return '-'
    const date = new Date(props.value)
    if (Number.isNaN(date.getTime())) return props.value
    // 后端以 epoch(1970)表示“从未更新”,不展示无意义的时间。
    if (date.getTime() <= 0) return '-'
    return date.toLocaleString(localeTag(), { hour12: false })
  })

  return {
      ...toRefs(props),
      formatted
    }
}

export type AdminDateTimeViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminDateTime>>>
