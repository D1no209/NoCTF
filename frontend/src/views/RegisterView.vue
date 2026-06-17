<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import LanguageSwitch from '@/components/LanguageSwitch.vue'
import { useAuthStore } from '@/stores/auth'
import { useRouter } from 'vue-router'

const { t } = useI18n()
const auth = useAuthStore()
const router = useRouter()

const userName = ref('')
const email = ref('')
const password = ref('')
const errors = ref({ userName: '', email: '', password: '' })
const serverError = ref('')
const loading = ref(false)

function validate() {
  errors.value = { userName: '', email: '', password: '' }
  let valid = true

  if (!userName.value) {
    errors.value.userName = t('validation.userNameRequired')
    valid = false
  }

  if (!email.value) {
    errors.value.email = t('validation.emailRequired')
    valid = false
  } else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.value)) {
    errors.value.email = t('validation.emailInvalid')
    valid = false
  }

  if (!password.value) {
    errors.value.password = t('validation.passwordRequired')
    valid = false
  } else if (password.value.length < 8) {
    errors.value.password = t('validation.passwordMin')
    valid = false
  }

  return valid
}

async function handleSubmit() {
  if (!validate()) return
  serverError.value = ''
  loading.value = true
  try {
    await auth.register(userName.value, email.value, password.value)
    await router.push('/login')
  } catch {
    serverError.value = t('errors.registerFailed')
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="min-h-screen bg-background flex items-center justify-center p-8 relative">
    <div class="absolute top-4 right-4">
      <LanguageSwitch />
    </div>
    <Card class="w-full max-w-sm">
      <CardHeader>
        <CardTitle class="text-2xl">{{ t('auth.registerTitle') }}</CardTitle>
        <CardDescription>{{ t('auth.registerSubtitle') }}</CardDescription>
      </CardHeader>
      <CardContent>
        <form class="flex flex-col gap-4" @submit.prevent="handleSubmit">
          <div class="flex flex-col gap-1.5">
            <Label for="userName">{{ t('auth.userName') }}</Label>
            <Input
              id="userName"
              v-model="userName"
              type="text"
              autocomplete="username"
              :placeholder="t('auth.userName')"
            />
            <p v-if="errors.userName" class="text-sm text-destructive">{{ errors.userName }}</p>
          </div>

          <div class="flex flex-col gap-1.5">
            <Label for="email">{{ t('auth.email') }}</Label>
            <Input
              id="email"
              v-model="email"
              type="email"
              autocomplete="email"
              :placeholder="t('auth.email')"
            />
            <p v-if="errors.email" class="text-sm text-destructive">{{ errors.email }}</p>
          </div>

          <div class="flex flex-col gap-1.5">
            <Label for="password">{{ t('auth.password') }}</Label>
            <Input
              id="password"
              v-model="password"
              type="password"
              autocomplete="new-password"
              :placeholder="t('auth.password')"
            />
            <p v-if="errors.password" class="text-sm text-destructive">{{ errors.password }}</p>
          </div>

          <p v-if="serverError" class="text-sm text-destructive">{{ serverError }}</p>

          <Button type="submit" :disabled="loading" class="w-full">
            {{ loading ? t('auth.registering') : t('auth.register') }}
          </Button>

          <p class="text-sm text-center text-muted-foreground">
            {{ t('auth.hasAccount') }}
            <RouterLink to="/login" class="text-primary underline-offset-4 hover:underline">
              {{ t('auth.signIn') }}
            </RouterLink>
          </p>
        </form>
      </CardContent>
    </Card>
  </div>
</template>
