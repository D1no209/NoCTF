
import { api } from '../../../../lib/api'
import { message as describeMessage } from '../../../../utils/i18n'
import type { UiMessage } from '../../../../utils/i18n'
import { proxyRefs } from 'vue'
import { markRaw } from 'vue'

import { KeyRound, RefreshCw, Send, ShieldCheck } from '@lucide/vue'
import { toast } from '../../../../utils/message-toast'

import type { NoCTFAPIEndpointsAdministrationPlatformAdminHumanVerificationConfigurationResponse, NoCTFAPIEndpointsAdministrationPlatformCapWorkloadConfigurationResponse, NoCTFAPIEndpointsAdministrationPlatformEmailVerificationConfigurationResponse, NoCTFAPIEndpointsAdministrationPlatformSmtpSecurityModeProtocol, NoCTFAPIEndpointsPlatformHumanVerificationProviderProtocol } from '../../../../api/models'
import AdminDateTimeComponent from '../../../admin/AdminDateTime.vue'

type EmailConfiguration = NoCTFAPIEndpointsAdministrationPlatformEmailVerificationConfigurationResponse
type HumanVerificationConfiguration = NoCTFAPIEndpointsAdministrationPlatformAdminHumanVerificationConfigurationResponse
type CapWorkloadConfiguration = NoCTFAPIEndpointsAdministrationPlatformCapWorkloadConfigurationResponse

/** Owns state, effects and commands for AdminPlatformEmailPage. */
export function useAdminPlatformEmailPage() {
  const { refresh: refreshPlatform } = usePlatform()

  const configuration = ref<EmailConfiguration | null>(null)

  const humanVerification = ref<HumanVerificationConfiguration | null>(null)

  const humanForm = reactive({
    enabled: false,
    runtimeEnabled: true,
    evaluationEnabled: true,
    provider: 'None' as NoCTFAPIEndpointsPlatformHumanVerificationProviderProtocol,
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
  const capWorkloadError = ref<UiMessage | null>(null)

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
      return translate('administration.label.capWorkloadRiskNormal')
    if (capExpectedHashAttempts.value < 100_000_000)
      return translate('administration.label.capWorkloadRiskHigh')
    return translate('administration.label.capWorkloadRiskExtreme')
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
    let error: unknown;
    const data = await api.api.v1.admin.platform.humanVerification.capWorkload.get().catch(cause => { error = cause; return undefined });
    capWorkloadLoading.value = false
    if (error || !data) {
      capWorkload.value = null
      capWorkloadError.value = parseApiError(
        error,
        describeMessage('administration.error.capWorkloadConfigurationUnavailable'),
      ).displayMessage
      return
    }
    syncCapWorkload(data)
  }

  async function saveCapWorkload(): Promise<void> {
    if (!capWorkloadDirty.value || !capWorkloadValid.value
      || capWorkloadSaving.value || humanVerificationDirty.value) return
    capWorkloadSaving.value = true
    let error: unknown;
    const data = await api.api.v1.admin.platform.humanVerification.capWorkload.put(capWorkloadRequest()).catch(cause => { error = cause; return undefined });
    capWorkloadSaving.value = false
    if (error || !data) {
      toast.error(parseApiError(
        error,
        describeMessage('administration.error.capWorkloadConfigurationUnavailable'),
      ).displayMessage)
      return
    }
    syncCapWorkload(data)
    toast.success(describeMessage('administration.label.capWorkloadConfigurationSaved'))
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
    return translate('administration.label.disabled')
  })

  watch(() => humanForm.provider, provider => {
    if (provider === 'None') humanForm.enabled = false
  })

  const loading = ref(true)

  const loadError = ref<UiMessage | null>(null)

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
    smtpSecurityMode: 'StartTls' as NoCTFAPIEndpointsAdministrationPlatformSmtpSecurityModeProtocol,
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
    let error: unknown;
    const data = await api.api.v1.admin.platform.configuration.get().catch(cause => { error = cause; return undefined });
    loading.value = false
    if (error || !data) {
      loadError.value = parseApiError(error).displayMessage
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
    let error: unknown;
    const data = await api.api.v1.admin.platform.configuration.patch({ humanVerification: humanVerificationRequest() }).catch(cause => { error = cause; return undefined });
    humanVerificationSaving.value = false
    if (error) {
      toast.error(parseApiError(error).displayMessage)
      return
    }
    if (data?.humanVerification) {
      humanVerification.value = data.humanVerification
      syncHumanVerification(data.humanVerification)
    }
    await refreshPlatform()
    if (humanForm.provider === 'Cap') await loadCapWorkload()
    toast.success(describeMessage('administration.label.humanVerificationConfigurationSaved'))
  }

  async function replaceHumanVerificationSecret(): Promise<void> {
    if (humanForm.provider === 'None' || !humanSecret.value
      || humanSecretSaving.value) return
    humanSecretSaving.value = true
    let error: unknown;
    const data = await api.api.v1.admin.platform.humanVerification.secret.put({
        provider: humanForm.provider,
        secret: humanSecret.value,
      }).catch(cause => { error = cause; return undefined });
    humanSecretSaving.value = false
    if (error) {
      toast.error(parseApiError(error).displayMessage)
      return
    }
    if (data) humanVerification.value = data
    humanSecret.value = ''
    humanSecretOpen.value = false
    toast.success(describeMessage('administration.label.humanVerificationSecretUpdated'))
  }

  async function save(): Promise<void> {
    if (!configuration.value) return
    saving.value = true
    let error: unknown;
    const data = await api.api.v1.admin.platform.configuration.patch({ emailVerification: emailVerificationRequest() }).catch(cause => { error = cause; return undefined });
    saving.value = false
    if (error) {
      toast.error(parseApiError(error).displayMessage)
      return
    }
    if (data?.emailVerification) {
      configuration.value = data.emailVerification
      syncForm(data.emailVerification)
    }
    toast.success(describeMessage("administration.label.emailVerificationConfigurationSaved"))
  }

  async function replacePassword(): Promise<void> {
    if (!configuration.value || !newPassword.value) return
    passwordSaving.value = true
    let error: unknown;
    const data = await api.api.v1.admin.platform.emailVerification.password.put({ password: newPassword.value }).catch(cause => { error = cause; return undefined });
    passwordSaving.value = false
    if (error) {
      toast.error(parseApiError(error).displayMessage)
      return
    }
    passwordOpen.value = false
    newPassword.value = ''
    if (data) configuration.value = data
    toast.success(describeMessage("administration.label.smtpPasswordUpdated"))
  }

  async function sendTest(): Promise<void> {
    sendingTest.value = true
    let error: unknown;
    await api.api.v1.admin.platform.emailVerification.test.post().catch(cause => { error = cause; return undefined });
    sendingTest.value = false
    if (error) {
      toast.error(parseApiError(error, describeMessage("administration.platformEmail.error.testEmailSendFailed")).displayMessage)
      return
    }
    toast.success(describeMessage("administration.platformEmail.description.testEmailSentAdministrator"))
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
