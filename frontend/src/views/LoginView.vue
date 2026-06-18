<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useAuthStore } from '@/stores/auth'
import { toast } from 'vue-sonner'
import * as z from 'zod'
import { useForm } from 'vee-validate'
import { toTypedSchema } from '@vee-validate/zod'

import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import {
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from '@/components/ui/form'
import AuthLayout from '@/components/layout/AuthLayout.vue'
import { Loader2 } from 'lucide-vue-next'

const { t } = useI18n()
const auth = useAuthStore()
const router = useRouter()
const loading = ref(false)

const formSchema = toTypedSchema(z.object({
  email: z.string().min(1, t('validation.emailRequired')).email(t('validation.emailInvalid')),
  password: z.string().min(8, t('validation.passwordMin')),
}))

const form = useForm({
  validationSchema: formSchema,
})

const onSubmit = form.handleSubmit(async (values) => {
  loading.value = true
  try {
    await auth.login(values.email, values.password)
    toast.success(t('auth.loginSuccess'))
    await router.push('/competitions')
  } catch (error: any) {
    toast.error(t('errors.loginFailed'))
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <AuthLayout :subtitle="t('auth.loginSubtitle')">
    <Card class="w-full border-none shadow-lg sm:border">
      <CardHeader class="space-y-1">
        <CardTitle class="text-2xl font-bold tracking-tight">{{ t('auth.loginTitle') }}</CardTitle>
        <CardDescription>{{ t('auth.loginSubtitle') }}</CardDescription>
      </CardHeader>
      <CardContent>
        <form @submit="onSubmit" class="space-y-4">
          <FormField v-slot="{ componentField }" name="email">
            <FormItem>
              <FormLabel>{{ t('auth.email') }}</FormLabel>
              <FormControl>
                <Input 
                  type="email" 
                  placeholder="name@example.com" 
                  v-bind="componentField" 
                  :disabled="loading"
                  autocomplete="email"
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

          <Button type="submit" class="w-full" :disabled="loading">
            <Loader2 v-if="loading" class="mr-2 h-4 w-4 animate-spin" />
            {{ loading ? t('auth.loggingIn') : t('auth.login') }}
          </Button>

          <div class="relative py-2">
            <div class="absolute inset-0 flex items-center">
              <span class="w-full border-t" />
            </div>
            <div class="relative flex justify-center text-xs uppercase">
              <span class="bg-card px-2 text-muted-foreground">{{ t('auth.noAccount') }}</span>
            </div>
          </div>

          <Button variant="outline" type="button" class="w-full" as-child :disabled="loading">
            <RouterLink to="/register">
              {{ t('auth.signUp') }}
            </RouterLink>
          </Button>
        </form>
      </CardContent>
    </Card>
  </AuthLayout>
</template>
