<script setup lang="ts">
import { CircleCheck, Loader2, MailCheck, MailWarning, RefreshCw } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'

import AuthLayout from '@/ui-v1/components/layout/AuthLayout.vue'
import { Alert, AlertDescription } from '@/ui-v1/components/ui/alert'
import { Button } from '@/ui-v1/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/ui-v1/components/ui/card'
import { Input } from '@/ui-v1/components/ui/input'
import { Label } from '@/ui-v1/components/ui/label'
import { useVerifyEmailPage } from '@/features/auth/useVerifyEmailPage'

const { t } = useI18n()
const {
  state,
  email,
  resending,
  retrySeconds,
  deliveryFailed,
  resend: resendVerification,
} = useVerifyEmailPage()

async function resend() {
  const outcome = await resendVerification()
  if (outcome === 'sent')
    toast.success(t('auth.verificationResent'))
  else if (outcome === 'failed')
    toast.error(t('errors.resendVerification'))
}
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
            <RouterLink to="/login">
              {{ t('auth.continueToLogin') }}
            </RouterLink>
          </Button>
        </template>

        <template v-else-if="state !== 'verifying'">
          <div class="space-y-2">
            <Label for="verification-email">{{ t('auth.email') }}</Label>
            <Input
              id="verification-email"
              v-model="email"
              type="email"
              autocomplete="email"
              placeholder="name@example.com"
              :disabled="resending"
              @keyup.enter="resend"
            />
          </div>

          <Button
            variant="outline"
            class="h-11 w-full"
            :disabled="resending || retrySeconds > 0 || !email.trim()"
            @click="resend"
          >
            <Loader2 v-if="resending" class="size-4 animate-spin" />
            <RefreshCw v-else class="size-4" />
            {{ retrySeconds > 0 ? t('auth.resendCountdown', { seconds: retrySeconds }) : t('auth.resendVerification') }}
          </Button>

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
