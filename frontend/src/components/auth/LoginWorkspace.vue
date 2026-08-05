<script setup lang="ts">
import { toTypedSchema } from '@vee-validate/zod'
import { AlertCircle, CircleCheck, Eye, EyeOff, Loader2 } from 'lucide-vue-next'
import { useForm } from 'vee-validate'
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { toast } from 'vue-sonner'
import * as z from 'zod'

import AuthLayout from '@/components/layout/AuthLayout.vue'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import {
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from '@/components/ui/form'
import { Input } from '@/components/ui/input'
import { Separator } from '@/components/ui/separator'
import { useAuthStore } from '@/stores/auth'

const { t } = useI18n()
const auth = useAuthStore()
const router = useRouter()
const route = useRoute()
const loading = ref(false)
const loginError = ref('')
const passwordVisible = ref(false)
const passwordResetSucceeded = computed(() => route.query.passwordReset === 'success')

const formSchema = toTypedSchema(z.object({
  email: z.string().trim().min(1, t('validation.loginIdentifierRequired')),
  password: z.string().min(8, t('validation.passwordMin')),
}))

const form = useForm({
  validationSchema: formSchema,
})

const onSubmit = form.handleSubmit(async (values) => {
  loading.value = true
  loginError.value = ''
  try {
    await auth.login(values.email, values.password)
    toast.success(t('auth.loginSuccess'))
    if (auth.emailVerified === false) {
      await router.push({ name: 'verify-email' })
      return
    }
    const redirect = typeof route.query.redirect === 'string' && route.query.redirect.startsWith('/')
      ? route.query.redirect
      : '/'
    await router.push(redirect)
  }
  catch {
    loginError.value = t('errors.loginFailed')
    toast.error(loginError.value)
  }
  finally {
    loading.value = false
  }
})
</script>

<template>
  <AuthLayout :subtitle="t('auth.loginSubtitle')">
    <Card class="w-full max-w-[470px]">
      <CardHeader class="border-b-2 border-border pb-3 text-center">
        <CardTitle class="text-2xl font-bold tracking-[0.08em]">
          {{ t('auth.loginTitle') }}
        </CardTitle>
        <p class="mt-2 text-sm text-muted-foreground">
          {{ t('auth.loginSubtitle') }}
        </p>
      </CardHeader>
      <CardContent class="pt-5">
        <form class="space-y-5" @submit="onSubmit">
          <Alert v-if="loginError" variant="destructive">
            <AlertCircle class="size-4" />
            <AlertDescription>{{ loginError }}</AlertDescription>
          </Alert>

          <Alert v-if="passwordResetSucceeded" variant="success" role="status">
            <CircleCheck class="size-4" />
            <AlertDescription>{{ t('auth.passwordResetSucceeded') }}</AlertDescription>
          </Alert>

          <FormField v-slot="{ componentField }" name="email">
            <FormItem>
              <FormLabel>{{ t('auth.loginIdentifier') }}</FormLabel>
              <FormControl>
                <Input
                  type="text"
                  :placeholder="t('auth.loginIdentifierPlaceholder')"
                  v-bind="componentField"
                  :disabled="loading"
                  autocomplete="username"
                />
              </FormControl>
              <FormMessage />
            </FormItem>
          </FormField>

          <FormField v-slot="{ componentField }" name="password">
            <FormItem>
              <div class="flex items-center justify-between gap-3">
                <FormLabel>{{ t('auth.password') }}</FormLabel>
                <RouterLink
                  :to="{ name: 'forgot-password' }"
                  class="text-sm font-bold underline-offset-4 hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                >
                  {{ t('auth.forgotPassword') }}
                </RouterLink>
              </div>
              <div class="relative">
                <FormControl>
                  <Input
                    :type="passwordVisible ? 'text' : 'password'"
                    class="pr-11"
                    v-bind="componentField"
                    :disabled="loading"
                    autocomplete="current-password"
                  />
                </FormControl>
                <button
                  type="button"
                  class="absolute inset-y-0 right-0 flex w-10 items-center justify-center text-muted-foreground transition-colors duration-150 hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-primary disabled:pointer-events-none disabled:opacity-50"
                  :aria-label="t(passwordVisible ? 'auth.hidePassword' : 'auth.showPassword')"
                  :aria-pressed="passwordVisible"
                  :disabled="loading"
                  @click="passwordVisible = !passwordVisible"
                >
                  <EyeOff v-if="passwordVisible" aria-hidden="true" class="size-4" />
                  <Eye v-else aria-hidden="true" class="size-4" />
                </button>
              </div>
              <FormMessage />
            </FormItem>
          </FormField>

          <Button type="submit" class="h-12 w-full text-base" :disabled="loading">
            <Loader2 v-if="loading" class="mr-2 h-4 w-4 animate-spin" />
            {{ loading ? t('auth.loggingIn') : t('auth.login') }}
          </Button>

          <div class="relative flex items-center py-2">
            <Separator class="shrink-0 flex-1" />
            <span class="bg-card px-2 text-xs uppercase text-muted-foreground">{{ t('auth.noAccount') }}</span>
            <Separator class="shrink-0 flex-1" />
          </div>

          <Button variant="outline" type="button" class="h-11 w-full" as-child :disabled="loading">
            <RouterLink to="/register">
              {{ t('auth.signUp') }}
            </RouterLink>
          </Button>
        </form>
      </CardContent>
    </Card>
  </AuthLayout>
</template>
