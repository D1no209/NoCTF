<script setup lang="ts">
import { CircleCheck, Loader2, LogIn, MailCheck, MailWarning, RefreshCw } from 'lucide-vue-next'
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { toast } from 'vue-sonner'
import { authApi } from '@/api/noctf'
import CommandButton from '../primitives/CommandButton.vue'
import CommandInput from '../primitives/CommandInput.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSignal from '../primitives/CommandSignal.vue'

type VerificationState = 'waiting' | 'verifying' | 'verified' | 'invalid'

const route = useRoute()
const router = useRouter()
const state = ref<VerificationState>('waiting')
const email = ref(typeof route.query.email === 'string' ? route.query.email : '')
const resending = ref(false)
const retrySeconds = ref(0)
const deliveryFailed = computed(() => route.query.delivery === 'failed')
let retryTimer: number | null = null

const stateIcon = computed(() => {
  if (state.value === 'verifying')
    return Loader2
  if (state.value === 'verified')
    return CircleCheck
  if (state.value === 'invalid')
    return MailWarning
  return MailCheck
})
const stateSignal = computed(() => {
  if (state.value === 'verifying')
    return { label: 'Verification in progress', tone: 'info' as const }
  if (state.value === 'verified')
    return { label: 'Email verified', tone: 'success' as const }
  if (state.value === 'invalid')
    return { label: 'Verification link invalid', tone: 'danger' as const }
  return { label: 'Verification pending', tone: 'warning' as const }
})

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
  await router.replace({ name: 'verify-email', query: email.value ? { email: email.value } : {} })
  try {
    await authApi.verifyEmail(token)
    state.value = 'verified'
  }
  catch {
    state.value = 'invalid'
  }
}

async function resend() {
  if (!email.value.trim() || resending.value || retrySeconds.value > 0)
    return

  resending.value = true
  try {
    await authApi.resendEmailVerification(email.value.trim())
    toast.success('Verification email sent again.')
    startRetryCountdown()
  }
  catch {
    toast.error('Unable to resend the verification email.')
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
  <CommandPanel class="v2-auth-card">
    <header class="v2-auth-card__heading">
      <span class="v2-auth-card__state-icon" :class="{ 'v2-auth-card__state-icon--spin': state === 'verifying' }">
        <component :is="stateIcon" class="size-5" />
      </span>
      <CommandSignal :label="stateSignal.label" :tone="stateSignal.tone" />
      <h1>{{ state === 'verified' ? 'Email verified' : 'Verify email' }}</h1>
      <p>
        <template v-if="state === 'verifying'">Checking the verification token with the account service.</template>
        <template v-else-if="state === 'verified'">The address is confirmed. You can sign in with your credentials now.</template>
        <template v-else-if="state === 'invalid'">This verification link is invalid or has expired. Request a fresh one below.</template>
        <template v-else>Open the link sent to your inbox, or request a new verification email.</template>
      </p>
    </header>

    <div class="v2-auth-card__form">
      <p v-if="deliveryFailed && state === 'waiting'" class="v2-auth-card__error" role="alert">
        <MailWarning class="size-4" />
        The platform could not deliver the first verification email. Use resend after checking the address.
      </p>

      <CommandButton v-if="state === 'verified'" label="Continue to sign in" @click="router.push('/login')">
        <template #icon><LogIn class="size-4" /></template>
      </CommandButton>

      <template v-else-if="state !== 'verifying'">
        <label>
          <span>Email</span>
          <CommandInput
            v-model="email"
            label="Email"
            type="email"
            placeholder="name@example.com"
            autocomplete="email"
            :disabled="resending"
            @enter="resend"
          />
        </label>
        <CommandButton
          :label="retrySeconds > 0 ? `Resend available in ${retrySeconds}s` : 'Resend verification email'"
          tone="outline"
          :disabled="resending || retrySeconds > 0 || !email.trim()"
          @click="resend"
        >
          <template #icon><RefreshCw class="size-4" /></template>
        </CommandButton>
        <CommandButton label="Back to sign in" tone="ghost" @click="router.push('/login')">
          <template #icon><LogIn class="size-4" /></template>
        </CommandButton>
      </template>
    </div>
  </CommandPanel>
</template>

<style scoped>
.v2-auth-card { padding: 22px; }
.v2-auth-card__heading { display: grid; justify-items: start; gap: 10px; }
.v2-auth-card__state-icon {
  display: grid;
  width: 44px;
  height: 44px;
  place-items: center;
  border-radius: 999px;
  background: var(--v2-surface);
  box-shadow: var(--v2-inset);
  color: var(--v2-primary);
}
.v2-auth-card__state-icon--spin { animation: v2-auth-spin 1.2s linear infinite; }
.v2-auth-card__heading h1 { margin: 0; color: var(--v2-text); font-size: 22px; font-weight: 600; letter-spacing: -0.01em; }
.v2-auth-card__heading p { margin: 0; color: var(--v2-text-muted); font-size: 13px; line-height: 1.55; }
.v2-auth-card__form { display: grid; gap: 14px; margin-top: 18px; }
.v2-auth-card__form label { display: grid; gap: 7px; }
.v2-auth-card__form label > span { color: var(--v2-text-muted); font-size: 11px; font-weight: 600; letter-spacing: 0.04em; }
.v2-auth-card__form :deep(.command-button) { width: 100%; }
.v2-auth-card__error {
  display: flex;
  align-items: center;
  gap: 8px;
  margin: 0;
  border-radius: 12px;
  padding: 10px 12px;
  background: var(--v2-surface);
  box-shadow: var(--v2-inset);
  color: var(--v2-warning);
  font-size: 12px;
}

@keyframes v2-auth-spin {
  to { transform: rotate(360deg); }
}

@media (prefers-reduced-motion: reduce) {
  .v2-auth-card__state-icon--spin { animation: none; }
}
</style>
