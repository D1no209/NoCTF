<script setup lang="ts">
import { CircleAlert, KeyRound, LogIn, UserPlus } from 'lucide-vue-next'
import { ref } from 'vue'
import { toast } from 'vue-sonner'
import CommandButton from '../primitives/CommandButton.vue'
import CommandInput from '../primitives/CommandInput.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSignal from '../primitives/CommandSignal.vue'
import { useLoginPage } from '@/features/auth/useLoginPage'

const email = ref('')
const password = ref('')
const loginError = ref('')
const { loading, login, goRegister } = useLoginPage()

async function submit() {
  if (loading.value)
    return
  loginError.value = ''
  if (!email.value.trim() || !password.value) {
    loginError.value = 'Enter both the account email and password.'
    return
  }

  const outcome = await login(email.value.trim(), password.value)
  if (outcome === 'success') {
    toast.success('Signed in successfully.')
  }
  else if (outcome === 'failed') {
    loginError.value = 'The service rejected these credentials.'
    toast.error(loginError.value)
  }
}
</script>

<template>
  <CommandPanel class="v2-auth-card">
    <header class="v2-auth-card__heading">
      <CommandSignal label="Account access" tone="info" />
      <h1>Sign in</h1>
      <p>Enter the operator credentials issued for this platform.</p>
    </header>

    <form class="v2-auth-card__form" @submit.prevent="submit">
      <p v-if="loginError" class="v2-auth-card__error" role="alert">
        <CircleAlert class="size-4" />
        {{ loginError }}
      </p>

      <label>
        <span>Email</span>
        <CommandInput
          v-model="email"
          label="Email"
          type="email"
          placeholder="name@example.com"
          autocomplete="email"
          :disabled="loading"
          @enter="submit"
        />
      </label>
      <label>
        <span>Password</span>
        <CommandInput
          v-model="password"
          label="Password"
          type="password"
          placeholder="••••••••"
          autocomplete="current-password"
          :disabled="loading"
          @enter="submit"
        />
      </label>

      <CommandButton
        :label="loading ? 'Signing in' : 'Sign in'"
        :disabled="loading"
        @click="submit"
      >
        <template #icon><LogIn class="size-4" /></template>
      </CommandButton>

      <CommandButton label="Create an account" tone="outline" :disabled="loading" @click="goRegister">
        <template #icon><UserPlus class="size-4" /></template>
      </CommandButton>

      <span class="v2-auth-card__hint">
        <KeyRound class="size-3.5" />
        Unverified accounts are routed to email verification automatically.
      </span>
    </form>
  </CommandPanel>
</template>

<style scoped>
.v2-auth-card { padding: 22px; }
.v2-auth-card__heading h1 { margin: 9px 0 0; color: var(--v2-text); font-size: 22px; font-weight: 600; letter-spacing: -0.01em; }
.v2-auth-card__heading p { margin: 7px 0 0; color: var(--v2-text-muted); font-size: 13px; line-height: 1.55; }
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
  color: var(--v2-danger);
  font-size: 12px;
}
.v2-auth-card__hint {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  color: var(--v2-text-faint);
  font-size: 11px;
}
</style>
