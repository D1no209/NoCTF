<script setup lang="ts">
import { toTypedSchema } from '@vee-validate/zod'
import { AlertCircle, Eye, EyeOff, KeyRound, Loader2 } from 'lucide-vue-next'
import { useForm } from 'vee-validate'
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import * as z from 'zod'

import { ApiError, authApi } from '@/api/noctf'
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
import { useAuthStore } from '@/stores/auth'

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const token = ref(typeof route.query.token === 'string' ? route.query.token : '')
const loading = ref(false)
const invalidLink = ref(!token.value)
const resetFailed = ref(false)
const newPasswordVisible = ref(false)
const confirmPasswordVisible = ref(false)

const form = useForm({
  validationSchema: toTypedSchema(z.object({
    newPassword: z.string()
      .min(1, t('validation.passwordRequired'))
      .min(8, t('validation.passwordMin')),
    confirmPassword: z.string().min(1, t('validation.passwordConfirmationRequired')),
  }).refine(values => values.newPassword === values.confirmPassword, {
    message: t('validation.passwordMismatch'),
    path: ['confirmPassword'],
  })),
})

const onSubmit = form.handleSubmit(async (values) => {
  if (!token.value || loading.value)
    return

  loading.value = true
  resetFailed.value = false
  try {
    await authApi.completePasswordReset(token.value, values.newPassword)
    token.value = ''
    auth.logout()
    await router.replace({ name: 'login', query: { passwordReset: 'success' } })
  }
  catch (error) {
    if (error instanceof ApiError && error.status === 400) {
      token.value = ''
      invalidLink.value = true
    }
    else {
      resetFailed.value = true
    }
  }
  finally {
    loading.value = false
  }
})

onMounted(() => {
  if (token.value)
    void router.replace({ name: 'reset-password' })
})
</script>

<template>
  <AuthLayout :subtitle="t('auth.resetPasswordSubtitle')">
    <Card class="w-full max-w-[470px]">
      <CardHeader class="border-b-2 border-border pb-3 text-center">
        <div class="mx-auto mb-3 flex size-12 items-center justify-center border-2 border-border bg-muted">
          <AlertCircle v-if="invalidLink" class="size-6 text-destructive" />
          <KeyRound v-else class="size-6" />
        </div>
        <CardTitle class="text-2xl font-bold tracking-[0.08em]">
          {{ invalidLink ? t('auth.resetLinkInvalidTitle') : t('auth.resetPasswordTitle') }}
        </CardTitle>
        <p class="mx-auto mt-2 max-w-[40ch] text-sm text-muted-foreground">
          {{ invalidLink ? t('auth.resetLinkInvalidDescription') : t('auth.resetPasswordSubtitle') }}
        </p>
      </CardHeader>

      <CardContent class="space-y-5 pt-5">
        <Alert v-if="resetFailed" variant="destructive" role="alert">
          <AlertCircle class="size-4" />
          <AlertDescription>{{ t('errors.completePasswordReset') }}</AlertDescription>
        </Alert>

        <template v-if="invalidLink">
          <Button class="h-12 w-full" as-child>
            <RouterLink :to="{ name: 'forgot-password' }">
              {{ t('auth.requestAnotherResetLink') }}
            </RouterLink>
          </Button>
        </template>

        <form v-else class="space-y-5" @submit="onSubmit">
          <FormField v-slot="{ componentField }" name="newPassword">
            <FormItem>
              <FormLabel>{{ t('auth.newPassword') }}</FormLabel>
              <div class="relative">
                <FormControl>
                  <Input
                    :type="newPasswordVisible ? 'text' : 'password'"
                    class="pr-11"
                    v-bind="componentField"
                    :disabled="loading"
                    autocomplete="new-password"
                  />
                </FormControl>
                <button
                  type="button"
                  class="absolute inset-y-0 right-0 flex w-10 items-center justify-center text-muted-foreground transition-colors duration-150 hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-primary disabled:pointer-events-none disabled:opacity-50"
                  :aria-label="t(newPasswordVisible ? 'auth.hidePassword' : 'auth.showPassword')"
                  :aria-pressed="newPasswordVisible"
                  :disabled="loading"
                  @click="newPasswordVisible = !newPasswordVisible"
                >
                  <EyeOff v-if="newPasswordVisible" aria-hidden="true" class="size-4" />
                  <Eye v-else aria-hidden="true" class="size-4" />
                </button>
              </div>
              <FormMessage />
            </FormItem>
          </FormField>

          <FormField v-slot="{ componentField }" name="confirmPassword">
            <FormItem>
              <FormLabel>{{ t('auth.confirmNewPassword') }}</FormLabel>
              <div class="relative">
                <FormControl>
                  <Input
                    :type="confirmPasswordVisible ? 'text' : 'password'"
                    class="pr-11"
                    v-bind="componentField"
                    :disabled="loading"
                    autocomplete="new-password"
                  />
                </FormControl>
                <button
                  type="button"
                  class="absolute inset-y-0 right-0 flex w-10 items-center justify-center text-muted-foreground transition-colors duration-150 hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-primary disabled:pointer-events-none disabled:opacity-50"
                  :aria-label="t(confirmPasswordVisible ? 'auth.hidePassword' : 'auth.showPassword')"
                  :aria-pressed="confirmPasswordVisible"
                  :disabled="loading"
                  @click="confirmPasswordVisible = !confirmPasswordVisible"
                >
                  <EyeOff v-if="confirmPasswordVisible" aria-hidden="true" class="size-4" />
                  <Eye v-else aria-hidden="true" class="size-4" />
                </button>
              </div>
              <FormMessage />
            </FormItem>
          </FormField>

          <p class="border-2 border-dashed border-border p-3 text-sm text-muted-foreground">
            {{ t('auth.resetPasswordSessionNotice') }}
          </p>

          <Button type="submit" class="h-12 w-full text-base" :disabled="loading">
            <Loader2 v-if="loading" class="size-4 animate-spin" />
            {{ loading ? t('auth.resettingPassword') : t('auth.resetPassword') }}
          </Button>
        </form>

        <Button variant="ghost" class="h-11 w-full" as-child>
          <RouterLink :to="{ name: 'login' }">
            {{ t('auth.backToLogin') }}
          </RouterLink>
        </Button>
      </CardContent>
    </Card>
  </AuthLayout>
</template>
