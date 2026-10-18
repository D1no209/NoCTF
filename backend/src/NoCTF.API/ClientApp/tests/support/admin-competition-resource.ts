import { computed } from 'vue'
import { createAdminCompetitionResource } from '../../app/features/admin/admin-competition-resource'

export function testAdminResource(load: (request: { path: { competitionId: string } }) => Promise<any>, id = 'contest') {
  const resource = createAdminCompetitionResource(key => load({ path: { competitionId: key } }))
  return {
    ...resource,
    competition: computed({ get: () => resource.data.value?.competition ?? null,
      set: value => { resource.data.value = { ...resource.data.value, competition: value ?? undefined } } }),
    read: () => resource.read('actor', id), refresh: () => resource.read('actor', id, true),
  }
}
