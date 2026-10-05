<script setup lang="ts">
import { toRefs } from 'vue'
import type { AuthLoginPageViewState } from '~/features/routes/auth/useAuthLoginPage'

const viewProps = defineProps<{ state: AuthLoginPageViewState }>()
const { authArtwork, configuration, loginName, password, error, pending, capVerification, capCanRetry, submitDisabled, submit, ssoProviders, ssoLoading, ssoPendingId, beginSso } = toRefs(viewProps.state)
</script>

<template>
  <div class="mx-auto flex w-full max-w-xl flex-col px-4 py-12">
    <div class="mb-8 flex flex-col items-center gap-2 text-center">
      <NuxtLink to="/" class="flex items-baseline gap-1.5 font-mono text-2xl font-semibold tracking-tight">
        <span class="text-primary">&gt;</span>
        <span>{{ configuration?.name ?? $t('common.label.noctf') }}</span>
        <span class="animate-blink text-primary">_</span>
      </NuxtLink>
      <p v-if="configuration?.description" class="text-sm text-muted-foreground">{{ configuration.description }}</p>
    </div>
    <Card class="auth-card">
      <img v-if="authArtwork" data-slot="auth-character-cutout" :data-corner="authArtwork.corner" :src="authArtwork.src" :width="authArtwork.width" :height="authArtwork.height" decoding="async" alt="" aria-hidden="true">
      <CardHeader class="relative z-10 pt-3">
        <CardTitle class="text-xl font-semibold">{{ $t('auth.login.action') }}</CardTitle>
      </CardHeader>
      <CardContent class="relative z-10">
        <UiForm @submit.prevent="submit">
          <FieldGroup>
            <Alert v-if="error" variant="destructive">
              <AlertDescription>{{ $message(error) }}</AlertDescription>
            </Alert>
            <Field>
              <FieldLabel for="login">{{ $t('common.label.usernameEmail') }}</FieldLabel>
              <Input id="login" v-model="loginName" autocomplete="username" required class="max-w-none" />
            </Field>
            <Field>
              <FieldLabel for="password">{{ $t('common.label.password') }}</FieldLabel>
              <PasswordInput id="password" v-model="password" autocomplete="current-password" required />
              <FieldDescription>
                <NuxtLink to="/auth/password-reset" class="underline">{{ $t('common.label.forgotPassword') }}</NuxtLink>
              </FieldDescription>
            </Field>
            <Field>
              <CapVerificationStatus
                v-if="capVerification"
                :key="capVerification.id"
                :state="capVerification.state"
                :progress="capVerification.progress"
                :label="capVerification.label"
              />
              <Button type="submit" :disabled="submitDisabled" class="w-full sm:mx-auto sm:w-2/3">
                <Spinner v-if="pending && !capCanRetry" data-icon="inline-start" />
                {{ capCanRetry ? $t('common.label.retry') : $t('auth.login.action') }}
              </Button>
            </Field>
          </FieldGroup>
        </UiForm>
        <template v-if="ssoLoading || ssoProviders.length">
          <div class="my-5 flex items-center gap-3" aria-hidden="true">
            <Separator class="flex-1" />
            <span class="text-xs text-muted-foreground">{{ $t('sso.orUseSingleSignOn') }}</span>
            <Separator class="flex-1" />
          </div>
          <div class="flex flex-col gap-2">
            <Skeleton v-if="ssoLoading" class="h-10 w-full" />
            <template v-else>
              <Button
                v-for="provider in ssoProviders"
                :key="provider.id"
                type="button"
                variant="outline"
                class="w-full"
                :disabled="Boolean(ssoPendingId)"
                @click="provider.id && beginSso(provider.id)"
              >
                <Spinner v-if="ssoPendingId === provider.id" data-icon="inline-start" />
                <img
                  v-else-if="provider.iconUrl"
                  :src="provider.iconUrl"
                  class="size-5 shrink-0 object-contain"
                  alt=""
                  aria-hidden="true"
                  decoding="async"
                  referrerpolicy="no-referrer"
                >
                {{ $t('sso.continueWith', { provider: provider.name ?? '' }) }}
              </Button>
            </template>
          </div>
        </template>
      </CardContent>
      <CardFooter class="relative z-10 justify-center text-sm text-muted-foreground">
        {{ $t('common.authLogin.description.donTAccountYet') }}
        <NuxtLink to="/auth/register" class="ml-1 underline">{{ $t('common.label.registerNow') }}</NuxtLink>
      </CardFooter>
    </Card>
  </div>
</template>

<style src="./auth-artwork.css"></style>
