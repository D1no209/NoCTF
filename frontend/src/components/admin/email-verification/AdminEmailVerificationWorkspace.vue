<script setup lang="ts">
import type { EmailVerificationConfiguration, SmtpSecurityMode } from '@/api/noctf'
import {
  CircleCheck,
  CircleX,
  KeyRound,
  Loader2,
  MailCheck,
  RefreshCw,
  Save,
  Send,
  ShieldCheck,
} from 'lucide-vue-next'
import { computed, onMounted, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'
import { platformAdminApi } from '@/api/noctf'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Separator } from '@/components/ui/separator'
import { Skeleton } from '@/components/ui/skeleton'

const { t } = useI18n()

const loading = ref(true)
const saving = ref(false)
const replacingPassword = ref(false)
const testing = ref(false)
const loadFailed = ref(false)
const password = ref('')
const saveFeedback = ref<'success' | 'error' | null>(null)

const form = reactive({
  enabled: false,
  publicBaseUrl: '',
  tokenLifetimeMinutes: 1440,
  resendCooldownSeconds: 60,
  passwordResetTokenLifetimeMinutes: 30,
  passwordResetCooldownSeconds: 60,
  passwordResetMaxRequestsPerHour: 3,
  smtpHost: '',
  smtpPort: 587,
  smtpSecurityMode: 'StartTls' as SmtpSecurityMode,
  smtpUserName: '',
  smtpPasswordConfigured: false,
  smtpFromAddress: '',
  smtpFromName: 'NoCTF',
  smtpTimeoutSeconds: 10,
  revision: 0,
  updatedAt: '',
})

const lastUpdated = computed(() => form.updatedAt
  ? new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' })
      .format(new Date(form.updatedAt))
  : t('admin.emailVerification.neverSaved'))
const usesAuthentication = computed(() => Boolean(form.smtpUserName.trim()))
const smtpReady = computed(() => Boolean(
  form.smtpHost.trim()
  && form.smtpFromAddress.trim()
  && (!usesAuthentication.value || form.smtpPasswordConfigured),
))

watch(form, () => {
  if (!saving.value)
    saveFeedback.value = null
}, { flush: 'sync' })

function applyConfiguration(configuration: EmailVerificationConfiguration) {
  form.enabled = configuration.enabled ?? false
  form.publicBaseUrl = configuration.publicBaseUrl ?? ''
  form.tokenLifetimeMinutes = configuration.tokenLifetimeMinutes ?? 1440
  form.resendCooldownSeconds = configuration.resendCooldownSeconds ?? 60
  form.passwordResetTokenLifetimeMinutes
    = configuration.passwordResetTokenLifetimeMinutes ?? 30
  form.passwordResetCooldownSeconds = configuration.passwordResetCooldownSeconds ?? 60
  form.passwordResetMaxRequestsPerHour = configuration.passwordResetMaxRequestsPerHour ?? 3
  form.smtpHost = configuration.smtpHost ?? ''
  form.smtpPort = configuration.smtpPort ?? 587
  form.smtpSecurityMode = configuration.smtpSecurityMode ?? 'StartTls'
  form.smtpUserName = configuration.smtpUserName ?? ''
  form.smtpPasswordConfigured = configuration.smtpPasswordConfigured ?? false
  form.smtpFromAddress = configuration.smtpFromAddress ?? ''
  form.smtpFromName = configuration.smtpFromName ?? 'NoCTF'
  form.smtpTimeoutSeconds = configuration.smtpTimeoutSeconds ?? 10
  form.revision = configuration.revision ?? 0
  form.updatedAt = configuration.updatedAt ?? ''
}

async function loadConfiguration() {
  loading.value = true
  loadFailed.value = false
  try {
    applyConfiguration(await platformAdminApi.emailVerificationConfiguration())
  }
  catch {
    loadFailed.value = true
    toast.error(t('errors.loadEmailVerificationConfiguration'))
  }
  finally {
    loading.value = false
  }
}

async function saveConfiguration() {
  if (saving.value)
    return

  saving.value = true
  saveFeedback.value = null
  try {
    applyConfiguration(await platformAdminApi.updateEmailVerificationConfiguration({
      enabled: form.enabled,
      publicBaseUrl: form.publicBaseUrl.trim(),
      tokenLifetimeMinutes: form.tokenLifetimeMinutes,
      resendCooldownSeconds: form.resendCooldownSeconds,
      passwordResetTokenLifetimeMinutes: form.passwordResetTokenLifetimeMinutes,
      passwordResetCooldownSeconds: form.passwordResetCooldownSeconds,
      passwordResetMaxRequestsPerHour: form.passwordResetMaxRequestsPerHour,
      smtpHost: form.smtpHost.trim(),
      smtpPort: form.smtpPort,
      smtpSecurityMode: form.smtpSecurityMode,
      smtpUserName: form.smtpUserName.trim(),
      smtpFromAddress: form.smtpFromAddress.trim(),
      smtpFromName: form.smtpFromName.trim(),
      smtpTimeoutSeconds: form.smtpTimeoutSeconds,
      expectedRevision: form.revision,
    }))
    saveFeedback.value = 'success'
    toast.success(t('admin.emailVerification.settingsSaved'))
  }
  catch {
    saveFeedback.value = 'error'
    toast.error(t('admin.emailVerification.settingsSaveFailed'))
  }
  finally {
    saving.value = false
  }
}

async function replacePassword() {
  if (!password.value || replacingPassword.value)
    return

  replacingPassword.value = true
  const replacement = password.value
  password.value = ''
  try {
    applyConfiguration(await platformAdminApi.replaceEmailVerificationPassword(
      replacement,
      form.revision,
    ))
    toast.success(t('admin.emailVerification.passwordReplaced'))
  }
  catch {
    toast.error(t('admin.emailVerification.passwordReplaceFailed'))
    await loadConfiguration()
  }
  finally {
    replacingPassword.value = false
  }
}

async function sendTest() {
  if (testing.value)
    return

  testing.value = true
  try {
    await platformAdminApi.sendEmailVerificationTest()
    toast.success(t('admin.emailVerification.testSent'))
  }
  catch {
    toast.error(t('admin.emailVerification.testFailed'))
  }
  finally {
    testing.value = false
  }
}

onMounted(loadConfiguration)
</script>

<template>
  <div class="mx-auto max-w-5xl space-y-6">
    <div class="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
      <div class="space-y-1">
        <div class="flex items-center gap-2">
          <h2 class="text-2xl font-bold tracking-tight">
            {{ t('admin.emailVerification.title') }}
          </h2>
          <Badge variant="destructive" class="rounded-none">
            {{ t('nav.admin') }}
          </Badge>
        </div>
        <p class="max-w-2xl text-sm text-muted-foreground">
          {{ t('admin.emailVerification.subtitle') }}
        </p>
      </div>
      <Button variant="outline" :disabled="loading" @click="loadConfiguration">
        <RefreshCw class="size-4" :class="{ 'animate-spin': loading }" />
        {{ t('common.refresh') }}
      </Button>
    </div>

    <div v-if="loading" class="space-y-4">
      <Skeleton class="h-24 rounded-none" />
      <Skeleton class="h-[520px] rounded-none" />
    </div>

    <Alert v-else-if="loadFailed" variant="destructive" class="rounded-none border-2">
      <AlertTitle>{{ t('state.failedToLoad') }}</AlertTitle>
      <AlertDescription class="mt-3">
        <Button variant="outline" size="sm" @click="loadConfiguration">
          {{ t('common.retry') }}
        </Button>
      </AlertDescription>
    </Alert>

    <template v-else>
      <div class="grid border-2 border-border bg-card sm:grid-cols-3">
        <div class="flex items-center gap-3 border-b-2 border-border p-4 sm:border-b-0 sm:border-r-2">
          <ShieldCheck class="size-5" />
          <div>
            <p class="text-[11px] font-bold uppercase tracking-[0.14em] text-muted-foreground">
              {{ t('admin.emailVerification.registrationGate') }}
            </p>
            <p class="font-bold">
              {{ form.enabled ? t('admin.emailVerification.enabled') : t('admin.emailVerification.disabled') }}
            </p>
          </div>
        </div>
        <div class="flex items-center gap-3 border-b-2 border-border p-4 sm:border-b-0 sm:border-r-2">
          <KeyRound class="size-5" />
          <div>
            <p class="text-[11px] font-bold uppercase tracking-[0.14em] text-muted-foreground">
              {{ t('admin.emailVerification.smtpCredential') }}
            </p>
            <p class="font-bold">
              {{ !usesAuthentication
                ? t('admin.emailVerification.notRequired')
                : form.smtpPasswordConfigured
                  ? t('admin.emailVerification.configured')
                  : t('admin.emailVerification.notConfigured') }}
            </p>
          </div>
        </div>
        <div class="flex items-center gap-3 p-4">
          <MailCheck class="size-5" />
          <div class="min-w-0">
            <p class="text-[11px] font-bold uppercase tracking-[0.14em] text-muted-foreground">
              {{ t('common.lastUpdated') }}
            </p>
            <p class="truncate font-bold">
              {{ lastUpdated }}
            </p>
          </div>
        </div>
      </div>

      <Card class="rounded-none border-2 shadow-none">
        <CardHeader class="border-b-2 border-border">
          <CardTitle>{{ t('admin.emailVerification.deliverySettings') }}</CardTitle>
          <p class="text-sm text-muted-foreground">
            {{ t('admin.emailVerification.deliverySettingsDescription') }}
          </p>
        </CardHeader>
        <CardContent class="space-y-7 pt-6">
          <label class="flex cursor-pointer items-start gap-3 border-2 border-border bg-muted/30 p-4">
            <input v-model="form.enabled" type="checkbox" class="mt-0.5 size-4 accent-foreground">
            <span>
              <span class="block font-bold">{{ t('admin.emailVerification.enableVerification') }}</span>
              <span class="mt-1 block text-sm text-muted-foreground">
                {{ t('admin.emailVerification.enableVerificationDescription') }}
              </span>
            </span>
          </label>

          <Alert v-if="form.enabled && usesAuthentication && !form.smtpPasswordConfigured" variant="warning" class="rounded-none border-2">
            <AlertTitle>{{ t('admin.emailVerification.passwordRequiredTitle') }}</AlertTitle>
            <AlertDescription>{{ t('admin.emailVerification.passwordRequiredDescription') }}</AlertDescription>
          </Alert>

          <section class="space-y-4">
            <div>
              <h3 class="font-bold">
                {{ t('admin.emailVerification.linkSettings') }}
              </h3>
              <p class="text-sm text-muted-foreground">
                {{ t('admin.emailVerification.publicBaseUrlHelp') }}
              </p>
            </div>
            <div class="grid gap-4 md:grid-cols-2">
              <div class="space-y-2 md:col-span-2">
                <Label for="email-public-url">{{ t('admin.emailVerification.publicBaseUrl') }}</Label>
                <Input id="email-public-url" v-model="form.publicBaseUrl" type="url" autocomplete="url" />
              </div>
              <div class="space-y-2">
                <Label for="email-token-lifetime">{{ t('admin.emailVerification.tokenLifetime') }}</Label>
                <Input id="email-token-lifetime" v-model.number="form.tokenLifetimeMinutes" type="number" min="5" max="10080" />
              </div>
              <div class="space-y-2">
                <Label for="email-resend-cooldown">{{ t('admin.emailVerification.resendCooldown') }}</Label>
                <Input id="email-resend-cooldown" v-model.number="form.resendCooldownSeconds" type="number" min="30" max="3600" />
              </div>
            </div>
          </section>

          <Separator />

          <section class="space-y-4">
            <div>
              <h3 class="font-bold">
                {{ t('admin.emailVerification.passwordResetPolicy') }}
              </h3>
              <p class="text-sm text-muted-foreground">
                {{ t('admin.emailVerification.passwordResetPolicyDescription') }}
              </p>
            </div>
            <div class="grid gap-4 md:grid-cols-3">
              <div class="space-y-2">
                <Label for="password-reset-token-lifetime">
                  {{ t('admin.emailVerification.passwordResetTokenLifetime') }}
                </Label>
                <Input
                  id="password-reset-token-lifetime"
                  v-model.number="form.passwordResetTokenLifetimeMinutes"
                  type="number"
                  min="5"
                  max="1440"
                />
              </div>
              <div class="space-y-2">
                <Label for="password-reset-cooldown">
                  {{ t('admin.emailVerification.passwordResetCooldown') }}
                </Label>
                <Input
                  id="password-reset-cooldown"
                  v-model.number="form.passwordResetCooldownSeconds"
                  type="number"
                  min="30"
                  max="3600"
                />
              </div>
              <div class="space-y-2">
                <Label for="password-reset-hourly-limit">
                  {{ t('admin.emailVerification.passwordResetHourlyLimit') }}
                </Label>
                <Input
                  id="password-reset-hourly-limit"
                  v-model.number="form.passwordResetMaxRequestsPerHour"
                  type="number"
                  min="1"
                  max="24"
                />
              </div>
            </div>
            <p class="border-2 border-dashed border-border p-3 text-sm text-muted-foreground">
              {{ t('admin.emailVerification.passwordResetIndependent') }}
            </p>
          </section>

          <Separator />

          <section class="space-y-4">
            <div>
              <h3 class="font-bold">
                {{ t('admin.emailVerification.smtpSettings') }}
              </h3>
              <p class="text-sm text-muted-foreground">
                {{ t('admin.emailVerification.smtpSettingsDescription') }}
              </p>
            </div>
            <div class="grid gap-4 md:grid-cols-2">
              <div class="space-y-2">
                <Label for="smtp-host">{{ t('admin.emailVerification.smtpHost') }}</Label>
                <Input id="smtp-host" v-model="form.smtpHost" autocomplete="off" />
              </div>
              <div class="space-y-2">
                <Label for="smtp-port">{{ t('admin.emailVerification.smtpPort') }}</Label>
                <Input id="smtp-port" v-model.number="form.smtpPort" type="number" min="1" max="65535" />
              </div>
              <div class="space-y-2">
                <Label for="smtp-user">{{ t('admin.emailVerification.smtpUsername') }}</Label>
                <Input id="smtp-user" v-model="form.smtpUserName" autocomplete="off" />
                <p class="text-xs text-muted-foreground">
                  {{ t('admin.emailVerification.smtpUsernameHelp') }}
                </p>
              </div>
              <div class="space-y-2">
                <Label for="smtp-timeout">{{ t('admin.emailVerification.timeout') }}</Label>
                <Input id="smtp-timeout" v-model.number="form.smtpTimeoutSeconds" type="number" min="1" max="120" />
              </div>
              <div class="space-y-2">
                <Label for="smtp-from-address">{{ t('admin.emailVerification.fromAddress') }}</Label>
                <Input id="smtp-from-address" v-model="form.smtpFromAddress" type="email" autocomplete="off" />
              </div>
              <div class="space-y-2">
                <Label for="smtp-from-name">{{ t('admin.emailVerification.fromName') }}</Label>
                <Input id="smtp-from-name" v-model="form.smtpFromName" autocomplete="off" />
              </div>
              <div class="space-y-2 md:col-span-2">
                <Label for="smtp-security-mode">{{ t('admin.emailVerification.securityMode') }}</Label>
                <Select v-model="form.smtpSecurityMode">
                  <SelectTrigger id="smtp-security-mode" class="rounded-none border-2">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="None">
                      {{ t('admin.emailVerification.securityNone') }}
                    </SelectItem>
                    <SelectItem value="SslOnConnect">
                      {{ t('admin.emailVerification.securitySslOnConnect') }}
                    </SelectItem>
                    <SelectItem value="StartTls">
                      {{ t('admin.emailVerification.securityStartTls') }}
                    </SelectItem>
                  </SelectContent>
                </Select>
                <p class="text-xs text-muted-foreground">
                  {{ t('admin.emailVerification.securityModeHelp') }}
                </p>
              </div>
            </div>
          </section>

          <div class="flex flex-col gap-3 border-t-2 border-border pt-5 sm:flex-row sm:justify-between">
            <Button
              variant="outline"
              :disabled="testing || !smtpReady"
              @click="sendTest"
            >
              <Loader2 v-if="testing" class="size-4 animate-spin" />
              <Send v-else class="size-4" />
              {{ t('admin.emailVerification.sendTest') }}
            </Button>
            <Button :disabled="saving" @click="saveConfiguration">
              <Loader2 v-if="saving" class="size-4 animate-spin" />
              <Save v-else class="size-4" />
              {{ t('admin.emailVerification.saveSettings') }}
            </Button>
          </div>

          <Alert
            v-if="saveFeedback"
            :variant="saveFeedback === 'success' ? 'success' : 'destructive'"
            class="rounded-none border-2"
            :role="saveFeedback === 'success' ? 'status' : 'alert'"
            :aria-live="saveFeedback === 'success' ? 'polite' : 'assertive'"
          >
            <div class="flex items-start gap-3">
              <CircleCheck v-if="saveFeedback === 'success'" class="mt-0.5 size-4 shrink-0" />
              <CircleX v-else class="mt-0.5 size-4 shrink-0" />
              <div>
                <AlertTitle>
                  {{ saveFeedback === 'success'
                    ? t('admin.emailVerification.settingsSaved')
                    : t('admin.emailVerification.settingsSaveFailed') }}
                </AlertTitle>
                <AlertDescription class="mt-1">
                  {{ saveFeedback === 'success'
                    ? t('admin.emailVerification.settingsSavedDescription')
                    : t('admin.emailVerification.settingsSaveFailedDescription') }}
                </AlertDescription>
              </div>
            </div>
          </Alert>
        </CardContent>
      </Card>

      <Card class="rounded-none border-2 shadow-none">
        <CardHeader class="border-b-2 border-border">
          <CardTitle>{{ t('admin.emailVerification.smtpPassword') }}</CardTitle>
          <p class="text-sm text-muted-foreground">
            {{ t('admin.emailVerification.passwordHelp') }}
          </p>
        </CardHeader>
        <CardContent class="pt-6">
          <form class="space-y-2" @submit.prevent="replacePassword">
            <Label for="smtp-password-replacement">{{ t('admin.emailVerification.enterPassword') }}</Label>
            <div class="flex flex-col gap-3 sm:flex-row">
              <Input
                id="smtp-password-replacement"
                v-model="password"
                type="password"
                autocomplete="new-password"
                :disabled="replacingPassword"
                class="flex-1"
              />
              <Button type="submit" :disabled="replacingPassword || !password">
                <Loader2 v-if="replacingPassword" class="size-4 animate-spin" />
                <KeyRound v-else class="size-4" />
                {{ t('admin.emailVerification.replacePassword') }}
              </Button>
            </div>
          </form>
        </CardContent>
      </Card>
    </template>
  </div>
</template>
