<script setup lang="ts">
import { CircleAlert, LogIn, UserPlus } from 'lucide-vue-next'
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { toast } from 'vue-sonner'
import { useAuthStore } from '@/stores/auth'
import CommandButton from '../primitives/CommandButton.vue'
import CommandInput from '../primitives/CommandInput.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSignal from '../primitives/CommandSignal.vue'

const auth = useAuthStore()
const router = useRouter()
const userName = ref('')
const email = ref('')
const password = ref('')
const loading = ref(false)
const registerError = ref('')

async function submit() {
  if (loading.value)
    return
  registerError.value = ''
  if (userName.value.trim().length < 2) {
    registerError.value = 'Choose a display name of at least 2 characters.'
    return
  }
  if (!email.value.trim()) {
    registerError.value = 'An email address is required.'
    return
  }
  if (password.value.length < 8) {
    registerError.value = 'The password needs at least 8 characters.'
    return
  }

  loading.value = true
  try {
    const result = await auth.register(userName.value.trim(), email.value.trim(), password.value)
    toast.success(`Account created for ${userName.value.trim()}.`)
    if (result.requiresEmailVerification) {
      await router.push({
        name: 'verify-email',
        query: {
          email: email.value.trim(),
          delivery: result.verificationEmailSent ? 'sent' : 'failed',
        },
      })
    }
    else {
      await router.push('/login')
    }
  }
  catch {
    registerError.value = 'The service could not create this account.'
    toast.error(registerError.value)
  }
  finally {
    loading.value = false
  }
}
</script>

<template>
  <CommandPanel class="v2-auth-card">
    <header class="v2-auth-card__heading">
      <CommandSignal label="Account provisioning" tone="success" />
      <h1>Create account</h1>
      <p>Register a new operator identity for this platform.</p>
    </header>

    <form class="v2-auth-card__form" @submit.prevent="submit">
      <p v-if="registerError" class="v2-auth-card__error" role="alert">
        <CircleAlert class="size-4" />
        {{ registerError }}
      </p>

      <label>
        <span>Display name</span>
        <CommandInput
          v-model="userName"
          label="Display name"
          type="text"
          placeholder="operator-name"
          autocomplete="username"
          :disabled="loading"
          @enter="submit"
        />
      </label>
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
          placeholder="At least 8 characters"
          autocomplete="new-password"
          :disabled="loading"
          @enter="submit"
        />
      </label>

      <CommandButton
        :label="loading ? 'Creating account' : 'Create account'"
        :disabled="loading"
        @click="submit"
      >
        <template #icon><UserPlus class="size-4" /></template>
      </CommandButton>

      <CommandButton label="Sign in instead" tone="ghost" :disabled="loading" @click="router.push('/login')">
        <template #icon><LogIn class="size-4" /></template>
      </CommandButton>
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
</style>
