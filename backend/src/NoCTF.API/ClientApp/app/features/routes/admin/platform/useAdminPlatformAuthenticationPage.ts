
import { api } from '../../../../lib/api'
import { message as describeMessage } from '../../../../utils/i18n'
import type { UiMessage } from '../../../../utils/i18n'
import { FlaskConical, Plus, RotateCw } from '@lucide/vue'
import { toast } from '../../../../utils/message-toast'

import type { NoCTFAPIEndpointsAdministrationPlatformSsoConfigurationResponse, NoCTFAPIEndpointsAdministrationPlatformSsoProviderResponse, NoCTFAPIEndpointsAdministrationPlatformCreateSsoProviderRequest, NoCTFAPIEndpointsAdministrationPlatformSsoProtocolProtocol } from '../../../../api/models'

type Configuration = NoCTFAPIEndpointsAdministrationPlatformSsoConfigurationResponse
type Provider = NoCTFAPIEndpointsAdministrationPlatformSsoProviderResponse

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
    protocol: 'Oidc' as NoCTFAPIEndpointsAdministrationPlatformSsoProtocolProtocol,
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
    let error: unknown;
    const data = await api.api.v1.admin.platform.sso.get().catch(cause => { error = cause; return undefined });
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
    let error: unknown;
    const data = await api.api.v1.admin.platform.sso.patch({
        enabled: globalForm.enabled,
        publicBaseUrl: globalForm.publicBaseUrl.trim(),
      }).catch(cause => { error = cause; return undefined });
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

  function providerRequest(): NoCTFAPIEndpointsAdministrationPlatformCreateSsoProviderRequest {
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
    let responseError: unknown;
    const response = await (providerForm.id
      ? api.api.v1.admin.platform.sso.providers.byProviderId(providerForm.id).put(providerRequest())
      : api.api.v1.admin.platform.sso.providers.post(providerRequest())).catch(cause => { responseError = cause; return undefined });
    providerSaving.value = false
    if (responseError || !response) {
      providerError.value = parseApiError(responseError, describeMessage('sso.providerSaveFailed')).displayMessage
      return
    }
    sync(response)
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
    let error: unknown;
    const data = await api.api.v1.admin.platform.sso.providers.byProviderId(providerId).secret.put({ secret: secret.value }).catch(cause => { error = cause; return undefined });
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
    let error: unknown;
    const data = await api.api.v1.admin.platform.sso.providers.byProviderId(provider.id).connectionTests.post().catch(cause => { error = cause; return undefined });
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
    let error: unknown;
    const data = await api.api.v1.admin.platform.sso.providers.byProviderId(provider.id).authenticationTests.post().catch(cause => { error = cause; return undefined });
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
    configuration, loading, loadError, globalForm, globalSaving,
    providerOpen, providerSaving, providerError, providerForm,
    secretOpen, secretProvider, secret, secretSaving, testingId,
    load, saveGlobal, openCreateProvider, openEditProvider, saveProvider,
    openSecret, replaceSecret, testConnection, testAuthentication,
  }
}

export type AdminPlatformAuthenticationPageViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useAdminPlatformAuthenticationPage>>
