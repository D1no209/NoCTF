<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'
import * as z from 'zod'
import { useForm } from 'vee-validate'
import { toTypedSchema } from '@vee-validate/zod'

import { Button } from '@/ui-v1/components/ui/button'
import { Input } from '@/ui-v1/components/ui/input'
import { Alert, AlertDescription } from '@/ui-v1/components/ui/alert'
import {
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from '@/ui-v1/components/ui/form'
import AuthLayout from '@/ui-v1/components/layout/AuthLayout.vue'
import { Card, CardContent, CardHeader, CardTitle } from '@/ui-v1/components/ui/card'
import { Separator } from '@/ui-v1/components/ui/separator'
import { AlertCircle, Loader2 } from 'lucide-vue-next'
import { useLoginPage } from '@/features/auth/useLoginPage'

const { t } = useI18n()
const loginError = ref('')
const { loading, login } = useLoginPage()

const formSchema = toTypedSchema(z.object({
  email: z.string().trim().min(1, t('validation.loginIdentifierRequired')),
  password: z.string().min(8, t('validation.passwordMin')),
}))

const form = useForm({
  validationSchema: formSchema,
})

const onSubmit = form.handleSubmit(async (values) => {
  loginError.value = ''
  const outcome = await login(values.email, values.password)
  if (outcome === 'success') {
    toast.success(t('auth.loginSuccess'))
  }
  else if (outcome === 'failed') {
    loginError.value = t('errors.loginFailed')
    toast.error(loginError.value)
  }
})
</script>

<template>
  <AuthLayout :subtitle="t('auth.loginSubtitle')">
    <Card class="w-full max-w-[470px]">
      <CardHeader class="border-b-2 border-border pb-3 text-center">
        <CardTitle class="text-2xl font-bold tracking-[0.08em]">{{ t('auth.loginTitle') }}</CardTitle>
        <p class="mt-2 text-sm text-muted-foreground">{{ t('auth.loginSubtitle') }}</p>
      </CardHeader>
      <CardContent class="pt-5">
        <form @submit="onSubmit" class="space-y-5">
          <Alert v-if="loginError" variant="destructive">
            <AlertCircle class="size-4" />
            <AlertDescription>{{ loginError }}</AlertDescription>
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
              <FormLabel>{{ t('auth.password') }}</FormLabel>
              <FormControl>
                <Input
                  type="password"
                  v-bind="componentField"
                  :disabled="loading"
                  autocomplete="current-password"
                />
              </FormControl>
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
