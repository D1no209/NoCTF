import { computed } from 'vue'
import { adminCompetitionResource as resource } from '../features/admin/admin-competition-resource'

export function useAdminCompetitionResource(competitionId: string) {
  const { user } = useAuth()
  const actor = () => user.value?.userId ?? ''
  const owner = actor()
  resource.reset(owner, competitionId)
  return {
    data: resource.data, fresh: resource.fresh, error: resource.error,
    competition: computed(() => resource.data.value?.competition ?? null),
    read: (_options?: { path: { competitionId: string } }) => resource.read(actor(), competitionId),
    refresh: () => resource.read(actor(), competitionId, true),
    accept: (value: Parameters<typeof resource.accept>[0]) => resource.accept(value, owner, competitionId),
    invalidate: resource.invalidate,
    clear: () => resource.clear(owner, competitionId),
  }
}
