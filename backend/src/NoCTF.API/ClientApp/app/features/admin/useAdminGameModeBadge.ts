import { toRefs } from 'vue'

/** Owns state, effects and commands for AdminGameModeBadge. */
export function useAdminGameModeBadge(props: Readonly<{ mode?: number | string | null }>) {
  const label = computed(() => {
    switch (props.mode) {
      case 0:
      case 'Ctf':
        return 'CTF'
      case 1:
      case 'Awd':
        return 'AWD'
      case 2:
      case 'Awdp':
        return 'AWDP'
      case 3:
      case 'Koh':
        return 'KoH'
      default:
        return '—'
    }
  })

  return {
      ...toRefs(props),
      label
    }
}

export type AdminGameModeBadgeViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminGameModeBadge>>>
