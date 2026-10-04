import type { UiMessage } from '../../utils/i18n'
import { markRaw, toRefs } from 'vue'

import { LockKeyhole } from '@lucide/vue'
import { adminGetPrivatePlatformUser, adminGetPrivateTeamMember } from '../../api'
import type { NoCtfapiEndpointsAdministrationPlatformPrivateAccountResponse } from '../../api'
import { summarizeAccountSources } from '../../utils/account-source-summary'
import AdminDateTimeComponent from '../admin/AdminDateTime.vue'

/** Owns state, effects and commands for PrivateAccountPanel. */
export function usePrivateAccountPanel(props: Readonly<Omit<{ userId: string, competitionId?: string, teamId?: string, showActivities?: boolean }, "showActivities"> & Required<Pick<{ userId: string, competitionId?: string, teamId?: string, showActivities?: boolean }, "showActivities">>>) {
  const data = ref<NoCtfapiEndpointsAdministrationPlatformPrivateAccountResponse | null>(null)

  const loading = ref(false)

  const error = ref<UiMessage | null>(null)

  let revision = 0

  const commonSources = computed(() => summarizeAccountSources(data.value?.activities ?? []))

  const kinds: Record<string, string> = { Registered: "common.label.registrationSuccessful", LoggedIn: "common.label.loginSuccessful", LoginFailed: "auth.login.failed", FlagSubmitted: "administration.label.flagSubmission", PatchUploaded: "common.label.patchUpload" }

  async function load() {
    const ticket = ++revision
    data.value = null
    loading.value = true
    error.value = null
    try {
      const result = props.competitionId && props.teamId
        ? await adminGetPrivateTeamMember({ path: { competitionId: props.competitionId, teamId: props.teamId, userId: props.userId } })
        : await adminGetPrivatePlatformUser({ path: { userId: props.userId } })
      if (ticket !== revision) return
      if (result.error) throw result.error
      data.value = result.data ?? null
    }
    catch (e) { if (ticket === revision) error.value = parseApiError(e).displayMessage }
    finally { if (ticket === revision) loading.value = false }
  }

  watch(() => [props.userId, props.competitionId, props.teamId], load, { immediate: true })

  onBeforeUnmount(() => revision++)

  const AdminDateTime = markRaw(AdminDateTimeComponent)

  return {
      ...toRefs(props),
      LockKeyhole,
      data,
      loading,
      error,
      commonSources,
      kinds,
      load,
      AdminDateTime
    }
}

export type PrivateAccountPanelViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof usePrivateAccountPanel>>>
