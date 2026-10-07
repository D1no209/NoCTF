import { message as describeMessage } from '../../../../utils/i18n'
import type { UiMessage } from '../../../../utils/i18n'
import { markRaw } from 'vue'
import MfaPlatformSettingsComponent from '../../../authentication/mfa/MfaPlatformSettings.vue'
import { FlaskConical, Plus, RotateCw } from '@lucide/vue'
import { toast } from '../../../../utils/message-toast'
import {
  adminPlatformSsoBeginAuthenticationTest,
  adminPlatformSsoCreateProvider,
  adminPlatformSsoGetConfiguration,
  adminPlatformSsoPatchConfiguration,
  adminPlatformSsoReplaceProviderSecret,
  adminPlatformSsoTestProviderConnection,
  adminPlatformSsoUpdateProvider,
} from '../../../../api'
import type {
  NoCtfapiEndpointsAdministrationPlatformSsoConfigurationResponse,
  NoCtfapiEndpointsAdministrationPlatformSsoProviderResponse,
  NoCtfapiEndpointsAdministrationPlatformSsoProviderWriteRequest,
  NoCtfapiEndpointsAdministrationPlatformSsoProtocolProtocol,
} from '../../../../api'

type Configuration = NoCtfapiEndpointsAdministrationPlatformSsoConfigurationResponse
type Provider = NoCtfapiEndpointsAdministrationPlatformSsoProviderResponse

export function useAdminPlatformAuthenticationPage() {
  const configuration = ref<Configuration | null>(null)
  const loading = ref(true)
  const loadError = ref<UiMessage | null>(null)
  const globalSaving = ref(false)
  const globalForm = reactive({ enabled: false, publicBaseUrl: '' })
  const providerOpen = ref(false)
  const providerSaving = ref(false)
  const providerError = ref<UiMessage | null>(null)
  const providerForm = reactive({
    id: '',
    name: '',
    iconUrl: '',
    protocol: 'Oidc' as NoCtfapiEndpointsAdministrationPlatformSsoProtocolProtocol,
    enabled: false,
    allowLogin: true,
    allowBinding: true,
    timeoutSeconds: 10,
    allowedHosts: '',
    issuer: '',
    discoveryUrl: '',
    clientId: '',
    scopes: 'openid\nprofile',
    readUserInfo: false,
    displayNameClaim: 'name',
    identityNamespace: '',
    loginUrl: '',
    serviceValidateUrl: '',
    displayNameAttribute: 'displayName',
  })
  const secretOpen = ref(false)
  const secretProvider = ref<Provider | null>(null)
  const secret = ref('')
  const secretSaving = ref(false)
  const testingId = ref<string | null>(null)

  function sync(value: Configuration) {
    configuration.value = value
    globalForm.enabled = value.enabled ?? false
    globalForm.publicBaseUrl = value.publicBaseUrl ?? ''
  }

  async function load() {
    loading.value = true
    loadError.value = null
    const { data, error } = await adminPlatformSsoGetConfiguration()
    loading.value = false
    if (error || !data) {
      loadError.value = parseApiError(error, describeMessage('sso.configurationUnavailable')).displayMessage
      return
    }
    sync(data)
  }

  async function saveGlobal() {
    if (globalSaving.value) return
    globalSaving.value = true
    const { data, error } = await adminPlatformSsoPatchConfiguration({
      body: {
        enabled: globalForm.enabled,
        publicBaseUrl: globalForm.publicBaseUrl.trim(),
      },
    })
    globalSaving.value = false
    if (error || !data) {
      toast.error(parseApiError(error, describeMessage('sso.configurationSaveFailed')).displayMessage)
      return
    }
    sync(data)
    toast.success(describeMessage('sso.configurationSaved'))
  }

  function resetProviderForm() {
    Object.assign(providerForm, {
      id: '', name: '', iconUrl: '', protocol: 'Oidc', enabled: false,
      allowLogin: true, allowBinding: true, timeoutSeconds: 10,
      allowedHosts: '', issuer: '', discoveryUrl: '', clientId: '',
      scopes: 'openid\nprofile', readUserInfo: false, displayNameClaim: 'name',
      identityNamespace: '', loginUrl: '', serviceValidateUrl: '',
      displayNameAttribute: 'displayName',
    })
    providerError.value = null
  }

  function openCreateProvider() {
    resetProviderForm()
    providerOpen.value = true
  }

  function openEditProvider(provider: Provider) {
    resetProviderForm()
    Object.assign(providerForm, {
      id: provider.id ?? '',
      name: provider.name ?? '',
      iconUrl: provider.iconUrl ?? '',
      protocol: provider.protocol ?? 'Oidc',
      enabled: provider.enabled ?? false,
      allowLogin: provider.allowLogin ?? false,
      allowBinding: provider.allowBinding ?? false,
      timeoutSeconds: provider.timeoutSeconds ?? 10,
      allowedHosts: (provider.allowedHosts ?? []).join('\n'),
      issuer: provider.oidc?.issuer ?? '',
      discoveryUrl: provider.oidc?.discoveryUrl ?? '',
      clientId: provider.oidc?.clientId ?? '',
      scopes: (provider.oidc?.scopes ?? ['openid', 'profile']).join('\n'),
      readUserInfo: provider.oidc?.readUserInfo ?? false,
      displayNameClaim: provider.oidc?.displayNameClaim ?? 'name',
      identityNamespace: provider.cas?.identityNamespace ?? '',
      loginUrl: provider.cas?.loginUrl ?? '',
      serviceValidateUrl: provider.cas?.serviceValidateUrl ?? '',
      displayNameAttribute: provider.cas?.displayNameAttribute ?? 'displayName',
    })
    providerOpen.value = true
  }

  function providerRequest(): NoCtfapiEndpointsAdministrationPlatformSsoProviderWriteRequest {
    const common = {
      name: providerForm.name.trim(),
      iconUrl: providerForm.iconUrl.trim() || null,
      protocol: providerForm.protocol,
      enabled: providerForm.enabled,
      allowLogin: providerForm.allowLogin,
      allowBinding: providerForm.allowBinding,
      timeoutSeconds: providerForm.timeoutSeconds,
      allowedHosts: providerForm.allowedHosts.split(/\r?\n/).map(value => value.trim()).filter(Boolean),
    }
    if (providerForm.protocol === 'Oidc') {
      return {
        ...common,
        oidc: {
          issuer: providerForm.issuer.trim(),
          discoveryUrl: providerForm.discoveryUrl.trim(),
          clientId: providerForm.clientId.trim(),
          scopes: providerForm.scopes.split(/\r?\n/).map(value => value.trim()).filter(Boolean),
          readUserInfo: providerForm.readUserInfo,
          displayNameClaim: providerForm.displayNameClaim.trim(),
        },
        cas: null,
      }
    }
    return {
      ...common,
      oidc: null,
      cas: {
        identityNamespace: providerForm.identityNamespace.trim(),
        loginUrl: providerForm.loginUrl.trim(),
        serviceValidateUrl: providerForm.serviceValidateUrl.trim(),
        displayNameAttribute: providerForm.displayNameAttribute.trim(),
      },
    }
  }

  async function saveProvider() {
    if (providerSaving.value) return
    providerSaving.value = true
    providerError.value = null
    const response = providerForm.id
      ? await adminPlatformSsoUpdateProvider({
          path: { providerId: providerForm.id },
          body: providerRequest(),
        })
      : await adminPlatformSsoCreateProvider({ body: providerRequest() })
    providerSaving.value = false
    if (response.error || !response.data) {
      providerError.value = parseApiError(response.error, describeMessage('sso.providerSaveFailed')).displayMessage
      return
    }
    sync(response.data)
    providerOpen.value = false
    toast.success(describeMessage('sso.providerSaved'))
  }

  function openSecret(provider: Provider) {
    secretProvider.value = provider
    secret.value = ''
    secretOpen.value = true
  }

  async function replaceSecret() {
    const providerId = secretProvider.value?.id
    if (!providerId || !secret.value || secretSaving.value) return
    secretSaving.value = true
    const { data, error } = await adminPlatformSsoReplaceProviderSecret({
      path: { providerId },
      body: { secret: secret.value },
    })
    secretSaving.value = false
    if (error || !data) {
      toast.error(parseApiError(error, describeMessage('sso.secretReplaceFailed')).displayMessage)
      return
    }
    sync(data)
    secret.value = ''
    secretOpen.value = false
    toast.success(describeMessage('sso.secretReplaced'))
  }

  async function testConnection(provider: Provider) {
    if (!provider.id || testingId.value) return
    testingId.value = provider.id
    const { data, error } = await adminPlatformSsoTestProviderConnection({
      path: { providerId: provider.id },
    })
    testingId.value = null
    if (error || !data?.succeeded) {
      toast.error(data?.failureCode ?? parseApiError(error, describeMessage('sso.connectionTestFailed')).displayMessage)
      return
    }
    toast.success(describeMessage('sso.connectionTestSuccessful'))
  }

  async function testAuthentication(provider: Provider) {
    if (!provider.id || testingId.value) return
    testingId.value = provider.id
    const { data, error } = await adminPlatformSsoBeginAuthenticationTest({
      path: { providerId: provider.id },
    })
    if (error || !data?.authorizationUrl) {
      testingId.value = null
      toast.error(parseApiError(error, describeMessage('sso.authenticationTestFailed')).displayMessage)
      return
    }
    window.location.assign(data.authorizationUrl)
  }

  onMounted(() => void load())

  return {
    Plus, RotateCw, FlaskConical,
    MfaPlatformSettings: markRaw(MfaPlatformSettingsComponent),
    configuration, loading, loadError, globalForm, globalSaving,
    providerOpen, providerSaving, providerError, providerForm,
    secretOpen, secretProvider, secret, secretSaving, testingId,
    load, saveGlobal, openCreateProvider, openEditProvider, saveProvider,
    openSecret, replaceSecret, testConnection, testAuthentication,
  }
}

export type AdminPlatformAuthenticationPageViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useAdminPlatformAuthenticationPage>>
