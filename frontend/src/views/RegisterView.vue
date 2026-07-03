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
  userName: z.string().min(2, t('validation.userNameRequired')),
  email: z.string().min(1, t('validation.emailRequired')).email(t('validation.emailInvalid')),
  password: z.string().min(8, t('validation.passwordMin')),
}))

const form = useForm({
  validationSchema: formSchema,
})

const onSubmit = form.handleSubmit(async (values) => {
  loading.value = true
  try {
    await auth.register(values.userName, values.email, values.password)
    toast.success(t('auth.registerSuccess', { name: values.userName }))
    await router.push('/login')
  } catch (error: any) {
    toast.error(t('errors.registerFailed'))
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <AuthLayout :subtitle="t('auth.registerSubtitle')">
    <Card class="noctf-panel w-full rounded-xl px-2 py-4 sm:px-4">
      <CardHeader class="space-y-2 text-center">
        <CardTitle class="text-3xl font-bold tracking-tight">{{ t('auth.registerTitle') }}</CardTitle>
        <CardDescription class="text-base">{{ t('auth.registerSubtitle') }}</CardDescription>
      </CardHeader>
      <CardContent>
        <form @submit="onSubmit" class="space-y-5">
          <FormField v-slot="{ componentField }" name="userName">
            <FormItem>
              <FormLabel>{{ t('auth.userName') }}</FormLabel>
              <FormControl>
                <Input 
                  type="text" 
                  placeholder="johndoe" 
                  v-bind="componentField" 
                  :disabled="loading"
                  autocomplete="username"
                />
              </FormControl>
              <FormMessage />
            </FormItem>
          </FormField>

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
                  autocomplete="new-password"
                />
              </FormControl>
              <FormMessage />
            </FormItem>
          </FormField>

          <Button type="submit" class="h-12 w-full text-base" :disabled="loading">
            <Loader2 v-if="loading" class="mr-2 h-4 w-4 animate-spin" />
            {{ loading ? t('auth.registering') : t('auth.register') }}
          </Button>

          <div class="relative py-2">
            <div class="absolute inset-0 flex items-center">
              <span class="w-full border-t" />
            </div>
            <div class="relative flex justify-center text-xs uppercase">
              <span class="bg-card px-2 text-muted-foreground">{{ t('auth.hasAccount') }}</span>
            </div>
          </div>

          <Button variant="outline" type="button" class="h-11 w-full" as-child :disabled="loading">
            <RouterLink to="/login">
              {{ t('auth.signIn') }}
            </RouterLink>
          </Button>
        </form>
      </CardContent>
    </Card>
  </AuthLayout>
</template>
