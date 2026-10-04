
import { api } from '../../lib/api'
import type { UiMessage } from '../../utils/i18n'
import { markRaw, toRefs } from 'vue'

import { LockKeyhole } from '@lucide/vue'

import type { NoCTFAPIEndpointsAdministrationPlatformPrivateAccountResponse } from '../../api/models'
import { summarizeAccountSources } from '../../utils/account-source-summary'
import AdminDateTimeComponent from '../admin/AdminDateTime.vue'

/** Owns state, effects and commands for PrivateAccountPanel. */
export function usePrivateAccountPanel(props: Readonly<Omit<{ userId: string, competitionId?: string | null, teamId?: string | null, showActivities?: boolean }, "showActivities"> & Required<Pick<{ userId: string, competitionId?: string | null, teamId?: string | null, showActivities?: boolean }, "showActivities">>>) {
  const data = ref<NoCTFAPIEndpointsAdministrationPlatformPrivateAccountResponse | null>(null)

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
      let resultError: unknown;
      const result = await (props.competitionId && props.teamId
        ? api.api.v1.admin.competitions.byCompetitionId(props.competitionId).teams.byTeamId(props.teamId).members.byUserId(props.userId).privateProfile.get()
        : api.api.v1.admin.platform.users.byUserId(props.userId).activity.get()).catch(cause => { resultError = cause; return undefined });
      if (ticket !== revision) return
      if (resultError) throw resultError
      data.value = result ?? null
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
