<script setup lang="ts">
import { CircleCheck, Loader2, MailCheck, MailWarning, RefreshCw } from 'lucide-vue-next'
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { toast } from 'vue-sonner'

import { authApi } from '@/api/noctf'
import AuthLayout from '@/components/layout/AuthLayout.vue'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { useAuthStore } from '@/stores/auth'

type VerificationState = 'waiting' | 'verifying' | 'verified' | 'invalid'

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const state = ref<VerificationState>('waiting')
const resending = ref(false)
const retrySeconds = ref(0)
const deliveryFailed = computed(() => route.query.delivery === 'failed')
const resendToastId = 'email-verification-resend'
let retryTimer: number | null = null

function startRetryCountdown() {
  retrySeconds.value = 60
  if (retryTimer !== null)
    window.clearInterval(retryTimer)
  retryTimer = window.setInterval(() => {
    retrySeconds.value = Math.max(0, retrySeconds.value - 1)
    if (retrySeconds.value === 0 && retryTimer !== null) {
      window.clearInterval(retryTimer)
      retryTimer = null
    }
  }, 1000)
}

async function verify(token: string) {
  state.value = 'verifying'
  await router.replace({ name: 'verify-email' })
  try {
    await authApi.verifyEmail(token)
    if (auth.isAuthenticated)
      await auth.refreshSession()
    state.value = 'verified'
  }
  catch {
    state.value = 'invalid'
  }
}

async function resend() {
  if (!auth.isAuthenticated || resending.value || retrySeconds.value > 0)
    return

  resending.value = true
  try {
    await authApi.resendEmailVerification()
    toast.success(t('auth.verificationResent'), { id: resendToastId })
    startRetryCountdown()
  }
  catch {
    toast.error(t('errors.resendVerification'), { id: resendToastId })
  }
  finally {
    resending.value = false
  }
}

onMounted(() => {
  const token = typeof route.query.token === 'string' ? route.query.token : ''
  if (token)
    void verify(token)
})

onBeforeUnmount(() => {
  if (retryTimer !== null)
    window.clearInterval(retryTimer)
})
</script>

<template>
  <AuthLayout :subtitle="t('auth.verifyEmailSubtitle')">
    <Card class="w-full max-w-[470px]">
      <CardHeader class="border-b-2 border-border pb-3 text-center">
        <div class="mx-auto mb-3 flex size-12 items-center justify-center border-2 border-border bg-muted">
          <Loader2 v-if="state === 'verifying'" class="size-6 animate-spin" />
          <CircleCheck v-else-if="state === 'verified'" class="size-6" />
          <MailWarning v-else-if="state === 'invalid'" class="size-6 text-destructive" />
          <MailCheck v-else class="size-6" />
        </div>
        <CardTitle class="text-2xl font-bold tracking-[0.08em]">
          {{ state === 'verified' ? t('auth.emailVerifiedTitle') : t('auth.verifyEmailTitle') }}
        </CardTitle>
        <p class="mx-auto mt-2 max-w-[38ch] text-sm text-muted-foreground" aria-live="polite">
          <template v-if="state === 'verifying'">
            {{ t('auth.verifyingEmail') }}
          </template>
          <template v-else-if="state === 'verified'">
            {{ t('auth.emailVerifiedDescription') }}
          </template>
          <template v-else-if="state === 'invalid'">
            {{ t('auth.verificationInvalid') }}
          </template>
          <template v-else>
            {{ t('auth.verifyEmailDescription') }}
          </template>
        </p>
      </CardHeader>

      <CardContent class="space-y-5 pt-5">
        <Alert v-if="deliveryFailed && state === 'waiting'" variant="warning">
          <MailWarning class="size-4" />
          <AlertDescription>{{ t('auth.verificationDeliveryFailed') }}</AlertDescription>
        </Alert>

        <template v-if="state === 'verified'">
          <Button class="h-12 w-full text-base" as-child>
            <RouterLink :to="auth.isAuthenticated ? '/competitions' : '/login'">
              {{ auth.isAuthenticated ? t('auth.continueToApp') : t('auth.continueToLogin') }}
            </RouterLink>
          </Button>
        </template>

        <template v-else-if="state !== 'verifying'">
          <Button
            v-if="auth.isAuthenticated"
            variant="outline"
            class="h-11 w-full"
            :disabled="resending || retrySeconds > 0"
            @click="resend"
          >
            <Loader2 v-if="resending" class="size-4 animate-spin" />
            <RefreshCw v-else class="size-4" />
            {{ retrySeconds > 0 ? t('auth.resendCountdown', { seconds: retrySeconds }) : t('auth.resendVerification') }}
          </Button>

          <p v-else class="border-2 border-dashed border-border p-3 text-sm text-muted-foreground">
            {{ t('auth.signInToResend') }}
          </p>

          <Button variant="ghost" class="h-11 w-full" as-child>
            <RouterLink to="/login">
              {{ t('auth.backToLogin') }}
            </RouterLink>
          </Button>
        </template>
      </CardContent>
    </Card>
  </AuthLayout>
</template>
