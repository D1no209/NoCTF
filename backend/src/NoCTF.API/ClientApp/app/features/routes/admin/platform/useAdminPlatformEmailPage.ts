import { proxyRefs } from 'vue'
import { markRaw } from 'vue'

import { KeyRound, RefreshCw, Send, ShieldCheck } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { adminPlatformGetConfiguration, adminPlatformPatchConfiguration, adminPlatformReplaceEmailVerificationPassword, adminPlatformSendEmailVerificationTest } from '../../../../api'
import type { NoCtfapiEndpointsAdministrationPlatformAdminHumanVerificationConfigurationResponse, NoCtfapiEndpointsAdministrationPlatformEmailVerificationConfigurationResponse, NoCtfapiEndpointsAdministrationPlatformSmtpSecurityModeProtocol } from '../../../../api'
import AdminDateTimeComponent from '../../../admin/AdminDateTime.vue'

type EmailConfiguration = NoCtfapiEndpointsAdministrationPlatformEmailVerificationConfigurationResponse
type HumanVerificationConfiguration = NoCtfapiEndpointsAdministrationPlatformAdminHumanVerificationConfigurationResponse

/** Owns state, effects and commands for AdminPlatformEmailPage. */
export function useAdminPlatformEmailPage() {
  const { refresh: refreshPlatform } = usePlatform()

  const configuration = ref<EmailConfiguration | null>(null)

  const humanVerification = ref<HumanVerificationConfiguration | null>(null)

  const humanVerificationEnabled = ref(false)

  const humanVerificationSaving = ref(false)

  const humanVerificationDirty = computed(() => humanVerification.value !== null
    && humanVerificationEnabled.value !== (humanVerification.value.enabled ?? false))

  const humanVerificationProviderLabel = computed(() => {
    if (humanVerification.value?.provider === 'Cap') return 'CAP'
    if (humanVerification.value?.provider === 'Turnstile') return 'Cloudflare Turnstile'
    return translate('ui.notConfigured')
  })

  const loading = ref(true)

  const loadError = ref<string | null>(null)

  const form = reactive({
    enabled: false,
    publicBaseUrl: '',
    tokenLifetimeMinutes: 30,
    resendCooldownSeconds: 60,
    passwordResetTokenLifetimeMinutes: 30,
    passwordResetCooldownSeconds: 60,
    passwordResetMaxRequestsPerHour: 5,
    smtpHost: '',
    smtpPort: 587,
    smtpSecurityMode: 'StartTls' as NoCtfapiEndpointsAdministrationPlatformSmtpSecurityModeProtocol,
    smtpUserName: '',
    smtpFromAddress: '',
    smtpFromName: '',
    smtpTimeoutSeconds: 15,
  })

  const saving = ref(false)

  const passwordOpen = ref(false)

  const newPassword = ref('')

  const passwordSaving = ref(false)

  const sendingTest = ref(false)

  function syncForm(value: EmailConfiguration): void {
    form.enabled = value.enabled ?? false
    form.publicBaseUrl = value.publicBaseUrl ?? ''
    form.tokenLifetimeMinutes = value.tokenLifetimeMinutes ?? 30
    form.resendCooldownSeconds = value.resendCooldownSeconds ?? 60
    form.passwordResetTokenLifetimeMinutes = value.passwordResetTokenLifetimeMinutes ?? 30
    form.passwordResetCooldownSeconds = value.passwordResetCooldownSeconds ?? 60
    form.passwordResetMaxRequestsPerHour = value.passwordResetMaxRequestsPerHour ?? 5
    form.smtpHost = value.smtpHost ?? ''
    form.smtpPort = value.smtpPort ?? 587
    form.smtpSecurityMode = value.smtpSecurityMode ?? 'StartTls'
    form.smtpUserName = value.smtpUserName ?? ''
    form.smtpFromAddress = value.smtpFromAddress ?? ''
    form.smtpFromName = value.smtpFromName ?? ''
    form.smtpTimeoutSeconds = value.smtpTimeoutSeconds ?? 15
  }

  async function load(): Promise<void> {
    loading.value = true
    loadError.value = null
    const { data, error } = await adminPlatformGetConfiguration()
    loading.value = false
    if (error || !data) {
      loadError.value = parseApiError(error).message
      return
    }
    humanVerification.value = data.humanVerification ?? null
    humanVerificationEnabled.value = data.humanVerification?.enabled ?? false
    configuration.value = data.emailVerification ?? null
    if (data.emailVerification) syncForm(data.emailVerification)
  }

  async function saveHumanVerification(): Promise<void> {
    if (!humanVerification.value || humanVerificationSaving.value
      || !humanVerificationDirty.value) return
    humanVerificationSaving.value = true
    const { data, error } = await adminPlatformPatchConfiguration({
      body: { humanVerification: { enabled: humanVerificationEnabled.value } },
    })
    humanVerificationSaving.value = false
    if (error) {
      toast.error(parseApiError(error).message)
      return
    }
    if (data?.humanVerification) {
      humanVerification.value = data.humanVerification
      humanVerificationEnabled.value = data.humanVerification.enabled ?? false
    }
    await refreshPlatform()
    toast.success(translate('ui.humanVerificationConfigurationSaved'))
  }

  async function save(): Promise<void> {
    if (!configuration.value) return
    saving.value = true
    const { data, error } = await adminPlatformPatchConfiguration({
      body: { emailVerification: { ...form } },
    })
    saving.value = false
    if (error) {
      toast.error(parseApiError(error).message)
      return
    }
    if (data?.emailVerification) configuration.value = data.emailVerification
    toast.success(translate("ui.emailVerificationConfigurationSaved"))
  }

  async function replacePassword(): Promise<void> {
    if (!configuration.value || !newPassword.value) return
    passwordSaving.value = true
    const { data, error } = await adminPlatformReplaceEmailVerificationPassword({
      body: { password: newPassword.value },
    })
    passwordSaving.value = false
    if (error) {
      toast.error(parseApiError(error).message)
      return
    }
    passwordOpen.value = false
    newPassword.value = ''
    if (data) configuration.value = data
    toast.success(translate("ui.smtpPasswordUpdated"))
  }

  async function sendTest(): Promise<void> {
    sendingTest.value = true
    const { error } = await adminPlatformSendEmailVerificationTest()
    sendingTest.value = false
    if (error) {
      toast.error(parseApiError(error, translate("ui.testEmailFailedToSend")).message)
      return
    }
    toast.success(translate("ui.theTestEmailHasBeenSentToTheCurrentAdministrator"))
  }

  onMounted(() => {
    void load()
  })

  const AdminDateTime = markRaw(AdminDateTimeComponent)

  const viewBindings = {
      KeyRound,
      RefreshCw,
      Send,
      ShieldCheck,
      configuration,
      humanVerification,
      humanVerificationEnabled,
      humanVerificationSaving,
      humanVerificationDirty,
      humanVerificationProviderLabel,
      loading,
      loadError,
      form,
      saving,
      passwordOpen,
      newPassword,
      passwordSaving,
      sendingTest,
      load,
      saveHumanVerification,
      save,
      replacePassword,
      sendTest,
      AdminDateTime
    }
  const viewState = proxyRefs(viewBindings)

  function onClickPasswordOpen(value: typeof viewState.passwordOpen) {
    viewState.passwordOpen = value
  }

  function onClickPasswordOpen2(value: typeof viewState.passwordOpen) {
    viewState.passwordOpen = value
  }

  return { ...viewBindings, onClickPasswordOpen, onClickPasswordOpen2 }
}

export type AdminPlatformEmailPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminPlatformEmailPage>>>
