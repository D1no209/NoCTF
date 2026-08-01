<script setup lang="ts">
import { toTypedSchema } from '@vee-validate/zod'
import { Eye, EyeOff, Loader2 } from 'lucide-vue-next'
import { useForm } from 'vee-validate'
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { toast } from 'vue-sonner'
import { createRegistrationSchema } from '@/components/auth/registrationValidation'

import AuthLayout from '@/components/layout/AuthLayout.vue'
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
const loading = ref(false)
const passwordVisible = ref(false)

const formSchema = toTypedSchema(createRegistrationSchema({
  userNameRequired: t('validation.userNameRequired'),
  userNameLength: t('validation.userNameLength'),
  userNameFormat: t('validation.userNameFormat'),
  emailRequired: t('validation.emailRequired'),
  emailInvalid: t('validation.emailInvalid'),
  passwordRequired: t('validation.passwordRequired'),
  passwordMin: t('validation.passwordMin'),
}))

const form = useForm({
  validationSchema: formSchema,
})

const onSubmit = form.handleSubmit(async (values) => {
  loading.value = true
  try {
    const result = await auth.register(values.userName, values.email, values.password)
    toast.success(t('auth.registerSuccess', { name: values.userName }))
    if (result.requiresEmailVerification) {
      await router.push({
        name: 'verify-email',
        query: {
          delivery: result.verificationEmailQueued ? 'sent' : 'failed',
        },
      })
    }
    else {
      await router.push('/login')
    }
  }
  catch {
    toast.error(t('errors.registerFailed'))
  }
  finally {
    loading.value = false
  }
})
</script>

<template>
  <AuthLayout :subtitle="t('auth.registerSubtitle')">
    <Card class="w-full max-w-[470px]">
      <CardHeader class="border-b-2 border-border pb-3 text-center">
        <CardTitle class="text-2xl font-bold tracking-[0.08em]">
          {{ t('auth.registerTitle') }}
        </CardTitle>
        <p class="mt-2 text-sm text-muted-foreground">
          {{ t('auth.registerSubtitle') }}
        </p>
      </CardHeader>
      <CardContent class="pt-5">
        <form class="space-y-5" @submit="onSubmit">
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
              <div class="relative">
                <FormControl>
                  <Input
                    :type="passwordVisible ? 'text' : 'password'"
                    class="pr-11"
                    v-bind="componentField"
                    :disabled="loading"
                    autocomplete="new-password"
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
