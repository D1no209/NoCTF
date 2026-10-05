import { message as describeMessage } from '../../utils/i18n'
import type { UiMessage } from '../../utils/i18n'
import { toRefs } from 'vue'

import { toast } from '../../utils/message-toast'
import { Crown, UserMinus } from '@lucide/vue'
import { patchCompetitionTeam, userProfileGet } from '../../api'
import type { NoCtfapiEndpointsAuthenticationPublicUserProfileResponse, NoCtfapiEndpointsTeamsTeamResponse } from '../../api'

/** Owns state, effects and commands for TeamMembers. */
export function useTeamMembers(props: Readonly<{
  competitionId: string
  team: NoCtfapiEndpointsTeamsTeamResponse
  /** 队长视角:可移除成员 */
  canManage?: boolean
}>,
emit: { (event: "changed", ...args: []): void }) {
  const profiles = ref<Record<string, NoCtfapiEndpointsAuthenticationPublicUserProfileResponse>>({})

  const loaded = ref(false)

  const loadError = ref<UiMessage | null>(null)

  const removing = ref<string | null>(null)

  async function loadProfiles() {
    const ids = props.team.memberIds ?? []
    loaded.value = false
    loadError.value = null
    const entries = await Promise.all(
      ids.map(async (id) => {
        const { data, error } = await userProfileGet({ path: { userId: id } })
        return { id, data, error }
      }),
    )
    profiles.value = Object.fromEntries(
      entries
        .filter(entry => !!entry.data)
        .map(entry => [entry.id, entry.data!] as const),
    )
    const failures = entries.filter(entry => entry.error || !entry.data).length
    if (failures > 0) {
      loadError.value = describeMessage("competitions.teamMembers.description.profilesTeamMembersCould", {
        count: failures,
      })
    }
    loaded.value = true
  }

  onMounted(loadProfiles)

  async function remove(userId: string) {
    removing.value = userId
    const { error } = await patchCompetitionTeam({
      path: { competitionId: props.competitionId, teamId: props.team.id! },
      body: {
        membership: {
          captainId: props.team.captainId!,
          memberIds: (props.team.memberIds ?? []).filter(id => id !== userId),
        },
      },
    })
    removing.value = null
    if (error) {
      toast.error(parseApiError(error, describeMessage("competitions.error.removeMemberFailed")).displayMessage)
      return
    }
    toast.success(describeMessage("competitions.label.memberRemoved"))
    emit('changed')
  }

  return {
      ...toRefs(props),
      Crown,
      UserMinus,
      profiles,
      loaded,
      loadError,
      removing,
      loadProfiles,
      remove
    }
}

export type TeamMembersViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useTeamMembers>>>
