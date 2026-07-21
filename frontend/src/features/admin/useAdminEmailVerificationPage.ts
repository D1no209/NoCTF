import { ref, watch } from 'vue'
import { useMutation, useQuery, useQueryClient } from '@tanstack/vue-query'
import { adminApi } from '@/api/noctf'
import { numberField } from '../shared/number'

export interface AdminEmailVerificationSettings {
  enabled: boolean
  publicBaseUrl: string
  tokenLifetimeMinutes: number
  resendCooldownSeconds: number
  smtpHost: string
  smtpPort: number
  smtpEnableSsl: boolean
  smtpUserName: string
  smtpPasswordConfigured: boolean
  smtpFromAddress: string
  smtpFromName: string
  smtpTimeoutSeconds: number
  persisted: boolean
  updatedAt?: string | null
}

export interface AdminEmailVerificationForm {
  enabled: boolean
  publicBaseUrl: string
  tokenLifetimeMinutes: string
  resendCooldownSeconds: string
  smtpHost: string
  smtpPort: string
  smtpEnableSsl: boolean
  smtpUserName: string
  smtpPassword: string
  smtpFromAddress: string
  smtpFromName: string
  smtpTimeoutSeconds: string
}

export function useAdminEmailVerificationPage() {
  const queryClient = useQueryClient()

  const form = ref<AdminEmailVerificationForm>({
    enabled: false,
    publicBaseUrl: '',
    tokenLifetimeMinutes: '1440',
    resendCooldownSeconds: '60',
    smtpHost: '',
    smtpPort: '587',
    smtpEnableSsl: true,
    smtpUserName: '',
    smtpPassword: '',
    smtpFromAddress: '',
    smtpFromName: 'NoCTF',
    smtpTimeoutSeconds: '10',
  })

  const settingsQuery = useQuery({
    queryKey: ['admin-email-verification'],
    queryFn: () => adminApi.emailVerificationSettings() as Promise<AdminEmailVerificationSettings>,
  })

  watch(settingsQuery.data, (settings) => {
    if (!settings)
      return
    form.value = {
      enabled: settings.enabled,
      publicBaseUrl: settings.publicBaseUrl,
      tokenLifetimeMinutes: String(settings.tokenLifetimeMinutes),
      resendCooldownSeconds: String(settings.resendCooldownSeconds),
      smtpHost: settings.smtpHost,
      smtpPort: String(settings.smtpPort),
      smtpEnableSsl: settings.smtpEnableSsl,
      smtpUserName: settings.smtpUserName,
      smtpPassword: '',
      smtpFromAddress: settings.smtpFromAddress,
      smtpFromName: settings.smtpFromName,
      smtpTimeoutSeconds: String(settings.smtpTimeoutSeconds),
    }
  }, { immediate: true })

  const saveMutation = useMutation({
    mutationFn: () => adminApi.updateEmailVerificationSettings({
      enabled: form.value.enabled,
      publicBaseUrl: form.value.publicBaseUrl,
      tokenLifetimeMinutes: numberField(form.value.tokenLifetimeMinutes),
      resendCooldownSeconds: numberField(form.value.resendCooldownSeconds),
      smtpHost: form.value.smtpHost,
      smtpPort: numberField(form.value.smtpPort),
      smtpEnableSsl: form.value.smtpEnableSsl,
      smtpUserName: form.value.smtpUserName,
      smtpPassword: form.value.smtpPassword || null,
      smtpFromAddress: form.value.smtpFromAddress,
      smtpFromName: form.value.smtpFromName,
      smtpTimeoutSeconds: numberField(form.value.smtpTimeoutSeconds),
    }),
    onSuccess: (settings) => {
      form.value.smtpPassword = ''
      queryClient.setQueryData(['admin-email-verification'], settings)
    },
  })

  const testMutation = useMutation({
    mutationFn: () => adminApi.testEmailVerificationSettings(),
  })

  return {
    settings: settingsQuery.data,
    isLoading: settingsQuery.isLoading,
    isError: settingsQuery.isError,
    error: settingsQuery.error,
    refetch: settingsQuery.refetch,
    form,
    saveMutation,
    testMutation,
  }
}
