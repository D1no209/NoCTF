import { proxyRefs } from 'vue'
import { markRaw } from 'vue'

import { KeyRound, RefreshCw, Send, ShieldCheck } from '@lucide/vue'
import { toast } from 'vue-sonner'
import { adminPlatformGetCapWorkloadConfiguration, adminPlatformGetConfiguration, adminPlatformPatchConfiguration, adminPlatformReplaceEmailVerificationPassword, adminPlatformReplaceHumanVerificationSecret, adminPlatformSendEmailVerificationTest, adminPlatformUpdateCapWorkloadConfiguration } from '../../../../api'
import type { NoCtfapiEndpointsAdministrationPlatformAdminHumanVerificationConfigurationResponse, NoCtfapiEndpointsAdministrationPlatformCapWorkloadConfigurationResponse, NoCtfapiEndpointsAdministrationPlatformEmailVerificationConfigurationResponse, NoCtfapiEndpointsAdministrationPlatformSmtpSecurityModeProtocol, NoCtfapiEndpointsPlatformHumanVerificationProviderProtocol } from '../../../../api'
import AdminDateTimeComponent from '../../../admin/AdminDateTime.vue'

type EmailConfiguration = NoCtfapiEndpointsAdministrationPlatformEmailVerificationConfigurationResponse
type HumanVerificationConfiguration = NoCtfapiEndpointsAdministrationPlatformAdminHumanVerificationConfigurationResponse
type CapWorkloadConfiguration = NoCtfapiEndpointsAdministrationPlatformCapWorkloadConfigurationResponse

/** Owns state, effects and commands for AdminPlatformEmailPage. */
export function useAdminPlatformEmailPage() {
  const { refresh: refreshPlatform } = usePlatform()

  const configuration = ref<EmailConfiguration | null>(null)

  const humanVerification = ref<HumanVerificationConfiguration | null>(null)

  const humanForm = reactive({
    enabled: false,
    runtimeEnabled: true,
    evaluationEnabled: true,
    provider: 'None' as NoCtfapiEndpointsPlatformHumanVerificationProviderProtocol,
    capServerUrl: '',
    capSiteKey: '',
    turnstileSiteKey: '',
    turnstileAllowedHostnames: '',
  })

  const humanVerificationSaving = ref(false)

  const humanVerificationSaved = ref('')

  const humanSecretOpen = ref(false)

  const humanSecret = ref('')

  const humanSecretSaving = ref(false)

  const capWorkload = ref<CapWorkloadConfiguration | null>(null)
  const capWorkloadForm = reactive({
    difficulty: 4,
    challengeCount: 80,
  })
  const capWorkloadSaved = ref('')
  const capWorkloadLoading = ref(false)
  const capWorkloadSaving = ref(false)
  const capWorkloadError = ref<string | null>(null)

  function capWorkloadRequest() {
    return {
      difficulty: capWorkloadForm.difficulty,
      challengeCount: capWorkloadForm.challengeCount,
    }
  }

  function syncCapWorkload(value: CapWorkloadConfiguration): void {
    capWorkload.value = value
    capWorkloadForm.difficulty = value.difficulty ?? 4
    capWorkloadForm.challengeCount = value.challengeCount ?? 80
    capWorkloadSaved.value = JSON.stringify(capWorkloadRequest())
  }

  const capWorkloadDirty = computed(() => capWorkload.value !== null
    && JSON.stringify(capWorkloadRequest()) !== capWorkloadSaved.value)
  const capWorkloadValid = computed(() => capWorkloadForm.difficulty >= 1
    && capWorkloadForm.difficulty <= 8
    && capWorkloadForm.challengeCount >= 1
    && capWorkloadForm.challengeCount <= 500)
  const capExpectedHashAttempts = computed(() => capWorkloadForm.challengeCount
    * 16 ** capWorkloadForm.difficulty)
  const capExpectedHashAttemptsLabel = computed(() =>
    new Intl.NumberFormat().format(capExpectedHashAttempts.value))
  const capWorkloadRiskLabel = computed(() => {
    if (capExpectedHashAttempts.value < 10_000_000)
      return translate('ui.capWorkloadRiskNormal')
    if (capExpectedHashAttempts.value < 100_000_000)
      return translate('ui.capWorkloadRiskHigh')
    return translate('ui.capWorkloadRiskExtreme')
  })
  const capWorkloadRiskVariant = computed(() => capExpectedHashAttempts.value >= 100_000_000
    ? 'destructive' as const
    : capExpectedHashAttempts.value >= 10_000_000
      ? 'outline' as const
      : 'secondary' as const)

  async function loadCapWorkload(): Promise<void> {
    if (humanForm.provider !== 'Cap') return
    capWorkloadLoading.value = true
    capWorkloadError.value = null
    const { data, error } = await adminPlatformGetCapWorkloadConfiguration()
    capWorkloadLoading.value = false
    if (error || !data) {
      capWorkload.value = null
      capWorkloadError.value = parseApiError(
        error,
        translate('ui.capWorkloadConfigurationUnavailable'),
      ).message
      return
    }
    syncCapWorkload(data)
  }

  async function saveCapWorkload(): Promise<void> {
    if (!capWorkloadDirty.value || !capWorkloadValid.value
      || capWorkloadSaving.value || humanVerificationDirty.value) return
    capWorkloadSaving.value = true
    const { data, error } = await adminPlatformUpdateCapWorkloadConfiguration({
      body: capWorkloadRequest(),
    })
    capWorkloadSaving.value = false
    if (error || !data) {
      toast.error(parseApiError(
        error,
        translate('ui.capWorkloadConfigurationUnavailable'),
      ).message)
      return
    }
    syncCapWorkload(data)
    toast.success(translate('ui.capWorkloadConfigurationSaved'))
  }

  function humanVerificationRequest() {
    return {
      enabled: humanForm.enabled && humanForm.provider !== 'None',
      runtimeEnabled: humanForm.runtimeEnabled,
      evaluationEnabled: humanForm.evaluationEnabled,
      provider: humanForm.provider,
      capServerUrl: humanForm.capServerUrl.trim(),
      capSiteKey: humanForm.capSiteKey.trim(),
      turnstileSiteKey: humanForm.turnstileSiteKey.trim(),
      turnstileAllowedHostnames: humanForm.turnstileAllowedHostnames
        .split(/\r?\n/)
        .map(hostname => hostname.trim())
        .filter(Boolean),
    }
  }

  function syncHumanVerification(value: HumanVerificationConfiguration): void {
    humanForm.enabled = value.enabled ?? false
    humanForm.runtimeEnabled = value.runtimeEnabled ?? true
    humanForm.evaluationEnabled = value.evaluationEnabled ?? true
    humanForm.provider = value.provider ?? 'None'
    humanForm.capServerUrl = value.capServerUrl ?? ''
    humanForm.capSiteKey = value.capSiteKey ?? ''
    humanForm.turnstileSiteKey = value.turnstileSiteKey ?? ''
    humanForm.turnstileAllowedHostnames = (value.turnstileAllowedHostnames ?? []).join('\n')
    humanVerificationSaved.value = JSON.stringify(humanVerificationRequest())
  }

  const humanVerificationDirty = computed(() => humanVerification.value !== null
    && JSON.stringify(humanVerificationRequest()) !== humanVerificationSaved.value)

  const selectedSecretConfigured = computed(() => humanForm.provider === 'Cap'
    ? humanVerification.value?.capSecretConfigured === true
    : humanForm.provider === 'Turnstile'
      ? humanVerification.value?.turnstileSecretConfigured === true
      : false)

  const humanVerificationReady = computed(() => {
    if (!humanVerificationDirty.value)
      return humanVerification.value?.ready === true
    return humanForm.provider === 'Cap'
      ? Boolean(humanForm.capServerUrl.trim()
        && humanForm.capSiteKey.trim()
        && selectedSecretConfigured.value)
      : humanForm.provider === 'Turnstile'
        ? Boolean(humanForm.turnstileSiteKey.trim()
          && humanVerificationRequest().turnstileAllowedHostnames.length
          && selectedSecretConfigured.value)
        : false
  })

  const humanVerificationProviderLabel = computed(() => {
    if (humanForm.provider === 'Cap') return 'CAP'
    if (humanForm.provider === 'Turnstile') return 'Cloudflare Turnstile'
    return translate('ui.disabled')
  })

  watch(() => humanForm.provider, provider => {
    if (provider === 'None') humanForm.enabled = false
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

  const savedEmailForm = ref('')

  function emailVerificationRequest() {
    return { ...form }
  }

  const emailDirty = computed(() => configuration.value !== null
    && JSON.stringify(emailVerificationRequest()) !== savedEmailForm.value)

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
    savedEmailForm.value = JSON.stringify(emailVerificationRequest())
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
    if (data.humanVerification) syncHumanVerification(data.humanVerification)
    configuration.value = data.emailVerification ?? null
    if (data.emailVerification) syncForm(data.emailVerification)
    if (humanForm.provider === 'Cap') await loadCapWorkload()
  }

  async function saveHumanVerification(): Promise<void> {
    if (!humanVerification.value || humanVerificationSaving.value
      || !humanVerificationDirty.value) return
    humanVerificationSaving.value = true
    const { data, error } = await adminPlatformPatchConfiguration({
      body: { humanVerification: humanVerificationRequest() },
    })
    humanVerificationSaving.value = false
    if (error) {
      toast.error(parseApiError(error).message)
      return
    }
    if (data?.humanVerification) {
      humanVerification.value = data.humanVerification
      syncHumanVerification(data.humanVerification)
    }
    await refreshPlatform()
    if (humanForm.provider === 'Cap') await loadCapWorkload()
    toast.success(translate('ui.humanVerificationConfigurationSaved'))
  }

  async function replaceHumanVerificationSecret(): Promise<void> {
    if (humanForm.provider === 'None' || !humanSecret.value
      || humanSecretSaving.value) return
    humanSecretSaving.value = true
    const { data, error } = await adminPlatformReplaceHumanVerificationSecret({
      body: {
        provider: humanForm.provider,
        secret: humanSecret.value,
      },
    })
    humanSecretSaving.value = false
    if (error) {
      toast.error(parseApiError(error).message)
      return
    }
    if (data) humanVerification.value = data
    humanSecret.value = ''
    humanSecretOpen.value = false
    toast.success(translate('ui.humanVerificationSecretUpdated'))
  }

  async function save(): Promise<void> {
    if (!configuration.value) return
    saving.value = true
    const { data, error } = await adminPlatformPatchConfiguration({
      body: { emailVerification: emailVerificationRequest() },
    })
    saving.value = false
    if (error) {
      toast.error(parseApiError(error).message)
      return
    }
    if (data?.emailVerification) {
      configuration.value = data.emailVerification
      syncForm(data.emailVerification)
    }
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
      humanForm,
      humanVerificationSaving,
      humanVerificationDirty,
      humanVerificationReady,
      humanVerificationProviderLabel,
      selectedSecretConfigured,
      humanSecretOpen,
      humanSecret,
      humanSecretSaving,
      capWorkload,
      capWorkloadForm,
      capWorkloadLoading,
      capWorkloadSaving,
      capWorkloadError,
      capWorkloadDirty,
      capWorkloadValid,
      capExpectedHashAttemptsLabel,
      capWorkloadRiskLabel,
      capWorkloadRiskVariant,
      loading,
      loadError,
      form,
      emailDirty,
      saving,
      passwordOpen,
      newPassword,
      passwordSaving,
      sendingTest,
      load,
      saveHumanVerification,
      replaceHumanVerificationSecret,
      loadCapWorkload,
      saveCapWorkload,
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

  function setHumanSecretOpen(value: boolean): void {
    if (viewState.humanSecretSaving) return
    viewState.humanSecretOpen = value
    if (!value) viewState.humanSecret = ''
  }

  return { ...viewBindings, onClickPasswordOpen, onClickPasswordOpen2, setHumanSecretOpen }
}

export type AdminPlatformEmailPageViewState = import('vue').ShallowUnwrapRef<Awaited<ReturnType<typeof useAdminPlatformEmailPage>>>
