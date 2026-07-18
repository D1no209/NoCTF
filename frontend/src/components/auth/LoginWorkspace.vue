<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { ApiError } from '@/api/noctf'
import { toast } from 'vue-sonner'
import * as z from 'zod'
import { useForm } from 'vee-validate'
import { toTypedSchema } from '@vee-validate/zod'

import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Alert, AlertDescription } from '@/components/ui/alert'
import {
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from '@/components/ui/form'
import AuthLayout from '@/components/layout/AuthLayout.vue'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Separator } from '@/components/ui/separator'
import { AlertCircle, Loader2 } from 'lucide-vue-next'

const { t } = useI18n()
const auth = useAuthStore()
const router = useRouter()
const route = useRoute()
const loading = ref(false)
const loginError = ref('')

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
    const redirect = typeof route.query.redirect === 'string' && route.query.redirect.startsWith('/')
      ? route.query.redirect
      : '/'
    await router.push(redirect)
  } catch (error: any) {
    if (error instanceof ApiError && error.status === 403) {
      const query = values.email.includes('@') ? { email: values.email } : undefined
      await router.push({ name: 'verify-email', query })
      return
    }
    loginError.value = t('errors.loginFailed')
    toast.error(loginError.value)
  } finally {
    loading.value = false
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
