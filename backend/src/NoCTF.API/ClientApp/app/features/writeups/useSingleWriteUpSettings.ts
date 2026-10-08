import { getChallengeWriteUpSettings, adminUpdateChallengeWriteUpSettings } from '~/api'
import type { NoCtfapiEndpointsChallengesWriteUpsChallengeWriteUpSettingsResponse } from '~/api'
import { message } from '~/utils/i18n'
import type { UiMessage } from '~/utils/i18n'
import { toast } from '~/utils/message-toast'

export function useSingleWriteUpSettings(props: Readonly<{ competitionId: string; competitionChallengeId?: string; canWrite: boolean }>, saved: () => void) {
  const data = ref<NoCtfapiEndpointsChallengesWriteUpsChallengeWriteUpSettingsResponse | null>(null)
  const loading = ref(false), pending = ref(false)
  const error = ref<UiMessage | null>(null)
  const enabled = ref(false), inherit = ref(true), percent = ref(20), deadlineHours = ref(24)
  const perChallenge = computed(() => !!props.competitionChallengeId)
  const retainedExample = computed(() => 500 - Math.ceil(500 * percent.value / 100))
  const deadlineAt = computed(() => data.value?.settings?.deadlineAt)
  let sequence = 0
  async function load() {
    const request = ++sequence; loading.value = true; error.value = null
    const result = await getChallengeWriteUpSettings({ path: { competitionId: props.competitionId },
      query: { competitionChallengeId: props.competitionChallengeId } })
    if (request !== sequence) return
    loading.value = false
    if (result.error || !result.data) { error.value = parseApiError(result.error, message('challengeWriteUp.settingsFailed')).displayMessage; return }
    data.value = result.data
    enabled.value = result.data.settings?.enabled ?? false
    percent.value = result.data.settings?.deductionPercent ?? 20
    inherit.value = result.data.settings?.challengeDeductionPercent == null
    deadlineHours.value = result.data.settings?.deadlineHours ?? 24
  }
  async function save() {
    if (!props.canWrite || pending.value || !data.value?.concurrencyStamp) return
    if (!Number.isInteger(percent.value) || percent.value < 0 || percent.value > 100
      || !Number.isInteger(deadlineHours.value) || deadlineHours.value < 0 || deadlineHours.value > 8760) {
      error.value = message('challengeWriteUp.error.InvalidContent'); return
    }
    pending.value = true; error.value = null
    const result = await adminUpdateChallengeWriteUpSettings({ path: { competitionId: props.competitionId }, body: {
      competitionChallengeId: props.competitionChallengeId,
      expectedStamp: data.value.concurrencyStamp,
      deductionPercent: perChallenge.value && inherit.value ? null : percent.value,
      enabled: perChallenge.value ? undefined : enabled.value,
      deadlineHours: perChallenge.value ? undefined : deadlineHours.value,
    } })
    pending.value = false
    if (result.error || !result.data) { error.value = parseApiError(result.error, message('challengeWriteUp.settingsFailed')).displayMessage; return }
    data.value = result.data
    enabled.value = result.data.settings?.enabled ?? false
    percent.value = result.data.settings?.deductionPercent ?? 20
    inherit.value = result.data.settings?.challengeDeductionPercent == null
    deadlineHours.value = result.data.settings?.deadlineHours ?? 24
    toast.success(message('challengeWriteUp.settingsSaved')); saved()
  }
  watch(() => [props.competitionId, props.competitionChallengeId], load, { immediate: true })
  onBeforeUnmount(() => sequence++)
  return { loading, pending, error, enabled, inherit, percent, deadlineHours, perChallenge, retainedExample, deadlineAt,
    canWrite: computed(() => props.canWrite), save, load }
}
export type SingleWriteUpSettingsState = import('vue').ShallowUnwrapRef<ReturnType<typeof useSingleWriteUpSettings>>
