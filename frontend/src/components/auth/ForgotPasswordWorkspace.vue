<script setup lang="ts">
import { toTypedSchema } from '@vee-validate/zod'
import { AlertCircle, CircleCheck, Loader2, Mail } from 'lucide-vue-next'
import { useForm } from 'vee-validate'
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import * as z from 'zod'

import { authApi } from '@/api/noctf'
import AuthLayout from '@/components/layout/AuthLayout.vue'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
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

const { t } = useI18n()
const loading = ref(false)
const requested = ref(false)
const requestFailed = ref(false)

const form = useForm({
  validationSchema: toTypedSchema(z.object({
    email: z.string().trim().min(1, t('validation.emailRequired')).email(t('validation.emailInvalid')),
  })),
})

const onSubmit = form.handleSubmit(async (values) => {
  loading.value = true
  requestFailed.value = false
  try {
    await authApi.requestPasswordReset(values.email.trim())
    requested.value = true
  }
  catch {
    requestFailed.value = true
  }
  finally {
    loading.value = false
  }
})
</script>

<template>
  <AuthLayout :subtitle="t('auth.forgotPasswordSubtitle')">
    <Card class="w-full max-w-[470px]">
      <CardHeader class="border-b-2 border-border pb-3 text-center">
        <div class="mx-auto mb-3 flex size-12 items-center justify-center border-2 border-border bg-muted">
          <CircleCheck v-if="requested" class="size-6" />
          <Mail v-else class="size-6" />
        </div>
        <CardTitle class="text-2xl font-bold tracking-[0.08em]">
          {{ requested ? t('auth.resetRequestedTitle') : t('auth.forgotPasswordTitle') }}
        </CardTitle>
        <p class="mx-auto mt-2 max-w-[40ch] text-sm text-muted-foreground">
          {{ requested ? t('auth.resetRequestedDescription') : t('auth.forgotPasswordSubtitle') }}
        </p>
      </CardHeader>

      <CardContent class="space-y-5 pt-5">
        <Alert v-if="requestFailed" variant="destructive" role="alert">
          <AlertCircle class="size-4" />
          <AlertDescription>{{ t('errors.requestPasswordReset') }}</AlertDescription>
        </Alert>

        <Alert v-if="requested" variant="success" role="status" aria-live="polite">
          <CircleCheck class="size-4" />
          <div>
            <AlertTitle>{{ t('auth.checkResetEmail') }}</AlertTitle>
            <AlertDescription>{{ t('auth.resetRequestPrivacyNotice') }}</AlertDescription>
          </div>
        </Alert>

        <form v-else class="space-y-5" @submit="onSubmit">
          <FormField v-slot="{ componentField }" name="email">
            <FormItem>
              <FormLabel>{{ t('auth.email') }}</FormLabel>
              <FormControl>
                <Input
                  type="email"
                  :placeholder="t('auth.emailPlaceholder')"
                  v-bind="componentField"
                  :disabled="loading"
                  autocomplete="email"
                />
              </FormControl>
              <FormMessage />
            </FormItem>
          </FormField>

          <Button type="submit" class="h-12 w-full text-base" :disabled="loading">
            <Loader2 v-if="loading" class="size-4 animate-spin" />
            {{ loading ? t('auth.requestingPasswordReset') : t('auth.sendPasswordResetLink') }}
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
