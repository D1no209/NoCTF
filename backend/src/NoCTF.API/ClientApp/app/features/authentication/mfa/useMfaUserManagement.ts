import { markRaw } from 'vue'
import { adminPlatformMfaChangeUserRequirement, adminPlatformMfaGrantUserRecovery } from '../../../api'
import type { UiMessage } from '../../../utils/i18n'
import { message } from '../../../utils/i18n'
import { toast } from '../../../utils/message-toast'
import { bindViewState } from '../../shared/view-state'
import StepUpView from '../../../components/views/authentication/MfaStepUpView.vue'
import { useMfaStepUp } from './useMfaStepUp'
export function useMfaUserManagement(props: { userId: string; required: boolean }) {
  const auth = useAuth()
  const available = computed(() => auth.user.value?.kind === 'Human')
  const required = ref(props.required)
  const reason = ref('')
  const error = shallowRef<UiMessage | null>(null)
  const stepUp = bindViewState(useMfaStepUp())
  watch(() => props.required, value => { required.value = value })
  async function saveRequirement() {
    const userId = props.userId; const value = required.value
    try {
      await stepUp.execute('ChangeAccountRequirement', userId, async () => {
        const result = await adminPlatformMfaChangeUserRequirement({ path: { userId }, body: { required: value } })
        if (result.error || !result.data) { error.value = parseApiError(result.error).displayMessage; return }
        toast.success(message('mfa.savedSettings'))
      })
    } catch (requestError) { error.value = parseApiError(requestError).displayMessage }
  }
  async function grantRecovery() {
    const userId = props.userId; const value = reason.value.trim(); if (!value) return
    try {
      await stepUp.execute('GrantRecovery', userId, async () => {
        const result = await adminPlatformMfaGrantUserRecovery({ path: { userId }, body: { reason: value } })
        if (result.error || !result.data) { error.value = parseApiError(result.error).displayMessage; return }
        reason.value = ''; toast.success(message('mfa.savedSettings'))
      })
    } catch (requestError) { error.value = parseApiError(requestError).displayMessage }
  }
  return { available, required, reason, error, stepUp, StepUpView: markRaw(StepUpView), saveRequirement, grantRecovery }
}
export type MfaUserManagementViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useMfaUserManagement>>
