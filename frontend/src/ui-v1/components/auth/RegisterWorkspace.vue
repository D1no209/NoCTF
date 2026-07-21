<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'
import * as z from 'zod'
import { useForm } from 'vee-validate'
import { toTypedSchema } from '@vee-validate/zod'

import { Button } from '@/ui-v1/components/ui/button'
import { Input } from '@/ui-v1/components/ui/input'
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
import { Loader2 } from 'lucide-vue-next'
import { useRegisterPage } from '@/features/auth/useRegisterPage'

const { t } = useI18n()
const { loading, register } = useRegisterPage()

const formSchema = toTypedSchema(z.object({
  userName: z.string().min(2, t('validation.userNameRequired')),
  email: z.string().min(1, t('validation.emailRequired')).email(t('validation.emailInvalid')),
  password: z.string().min(8, t('validation.passwordMin')),
}))

const form = useForm({
  validationSchema: formSchema,
})

const onSubmit = form.handleSubmit(async (values) => {
  const outcome = await register(values.userName, values.email, values.password)
  if (outcome === 'failed')
    toast.error(t('errors.registerFailed'))
  else
    toast.success(t('auth.registerSuccess', { name: values.userName }))
})
</script>

<template>
  <AuthLayout :subtitle="t('auth.registerSubtitle')">
    <Card class="w-full max-w-[470px]">
      <CardHeader class="border-b-2 border-border pb-3 text-center">
        <CardTitle class="text-2xl font-bold tracking-[0.08em]">{{ t('auth.registerTitle') }}</CardTitle>
        <p class="mt-2 text-sm text-muted-foreground">{{ t('auth.registerSubtitle') }}</p>
      </CardHeader>
      <CardContent class="pt-5">
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

          <div class="relative flex items-center py-2">
            <Separator class="shrink-0 flex-1" />
            <span class="bg-card px-2 text-xs uppercase text-muted-foreground">{{ t('auth.hasAccount') }}</span>
            <Separator class="shrink-0 flex-1" />
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
