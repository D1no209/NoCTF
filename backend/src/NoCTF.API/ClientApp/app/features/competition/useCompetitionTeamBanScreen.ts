import { computed, onScopeDispose, ref, watch } from 'vue'
import { getMyTeamBanCase, submitTeamBanAppeal } from '../../api'
import type { NoCtfapiEndpointsTeamsMyTeamBanCaseResponse } from '../../api'
import logoUrl from '../../assets/images/brand/noctf-team-ban-logo.png'
import { validateAppealStatement } from '../../lib/participant-form-validation'
import { message } from '../../utils/i18n'
import type { UiMessage } from '../../utils/i18n'
import { parseApiError } from '../../utils/api-error'

export function useCompetitionTeamBanScreen(props: Readonly<{ competitionId: string; teamId: string; teamName?: string; teamAvatarUrl?: string | null }>, refreshTeam: () => void = () => {}) {
  const banCase = ref<NoCtfapiEndpointsTeamsMyTeamBanCaseResponse | null>(null)
  const loading = ref(true)
  const error = ref<UiMessage | null>(null)
  const appealOpen = ref(false)
  const statement = ref('')
  const pending = ref(false)
  const appealError = ref<UiMessage | null>(null)
  let request: AbortController | undefined
  const isCheatingBan = computed(() => banCase.value?.source === 'CheatIncident')
  const appealStatus = computed(() => banCase.value?.appeal?.status)
  const canAppeal = computed(() => banCase.value?.canAppeal === true)
  const teamName = computed(() => props.teamName ?? banCase.value?.teamName ?? '')
  const teamAvatarUrl = computed(() => props.teamAvatarUrl ?? undefined)
  const teamInitials = computed(() => Array.from(teamName.value.trim()).slice(0, 2).join('').toUpperCase())
  async function refresh() {
    request?.abort()
    const current = new AbortController()
    request = current
    loading.value = true
    error.value = null
    try {
      const { data, error: failure } = await getMyTeamBanCase({ path: { competitionId: props.competitionId }, signal: current.signal })
      if (current.signal.aborted) return
      loading.value = false
      if (failure || !data) error.value = parseApiError(failure, message('teamBanScreen.loadFailed')).displayMessage
      else if (data.teamId === props.teamId && data.isCurrentlyBanned) banCase.value = data
    }
    catch (failure) {
      if (current.signal.aborted) return
      loading.value = false
      error.value = parseApiError(failure, message('teamBanScreen.loadFailed')).displayMessage
    }
  }
  function refreshScreen() {
    refreshTeam()
    return refresh()
  }
  function setAppealOpen(open: boolean) {
    if (pending.value) return
    appealOpen.value = open
    appealError.value = null
  }
  async function submitAppeal() {
    if (pending.value || !canAppeal.value) return
    const validation = validateAppealStatement(statement.value)
    if (validation) { appealError.value = message('teamBanScreen.statementRequired'); return }
    pending.value = true
    appealError.value = null
    try {
      const { error: failure } = await submitTeamBanAppeal({ path: { competitionId: props.competitionId }, body: { statement: statement.value.trim() } })
      if (failure) { appealError.value = parseApiError(failure, message('teamBanScreen.appealFailed')).displayMessage; return }
      appealOpen.value = false
      statement.value = ''
      await refresh()
    }
    catch (failure) { appealError.value = parseApiError(failure, message('teamBanScreen.appealFailed')).displayMessage }
    finally { pending.value = false }
  }
  watch(() => [props.competitionId, props.teamId], () => { banCase.value = null; void refresh() }, { immediate: true })
  onScopeDispose(() => request?.abort())
  return { logoUrl, teamName, teamAvatarUrl, teamInitials, loading, error, isCheatingBan, canAppeal, appealStatus,
    appealOpen, statement, pending, appealError, refresh, refreshScreen, setAppealOpen, submitAppeal }
}

export type CompetitionTeamBanScreenViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useCompetitionTeamBanScreen>>
