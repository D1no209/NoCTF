import { computed, markRaw, toRefs } from 'vue'
import { EyeOff } from '@lucide/vue'
import type { CompetitionGroup, CompetitionSidebarOption } from './competition-browser'
import LifecycleBadgeComponent from './LifecycleBadge.vue'

export interface CompetitionSidebarProps {
  loading: boolean
  options: CompetitionSidebarOption[]
  selectedId: string | null
  group: CompetitionGroup
  counts: Record<CompetitionGroup, number>
  showDeleted: boolean
}
type SidebarEmit = {
  (event: 'update:group', value: CompetitionGroup): void
  (event: 'select', value: string): void
}

export function useCompetitionSidebar(props: Readonly<CompetitionSidebarProps>, emit: SidebarEmit) {
  const group = computed({ get: () => props.group, set: value => emit('update:group', value) })
  const select = (value: string) => emit('select', value)
  function setGroup(value: unknown) {
    if ((value === 'running' || value === 'upcoming' || value === 'finished' || value === 'deleted') && value !== props.group)
      emit('update:group', value)
  }
  return { ...toRefs(props), EyeOff, group, setGroup, select, LifecycleBadge: markRaw(LifecycleBadgeComponent) }
}
export type CompetitionSidebarViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useCompetitionSidebar>>
