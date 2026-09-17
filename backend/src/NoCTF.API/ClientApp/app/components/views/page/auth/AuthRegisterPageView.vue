<script setup lang="ts">
import { toRefs } from 'vue'
import type { AuthRegisterPageViewState } from '~/features/routes/auth/useAuthRegisterPage'

const viewProps = defineProps<{ state: AuthRegisterPageViewState }>()
const { authArtwork, configuration, userName, email, password, confirmPassword, error, pending, registered, resendPending, resendDone, resendError, submit, resendVerification } = toRefs(viewProps.state)
</script>

<template>
  <div class="mx-auto flex w-full max-w-xl flex-col px-4 py-12">
    <div class="mb-8 flex flex-col items-center gap-2 text-center">
      <NuxtLink to="/" class="flex items-baseline gap-1.5 font-mono text-2xl font-semibold tracking-tight">
        <span class="text-primary">&gt;</span>
        <span>{{ configuration?.name ?? $t('ui.noctf') }}</span>
        <span class="animate-blink text-primary">_</span>
      </NuxtLink>
      <p v-if="configuration?.description" class="text-sm text-muted-foreground">{{ configuration.description }}</p>
    </div>
    <Card v-if="registered" class="auth-card">
      <img v-if="authArtwork" data-slot="auth-character-cutout" :data-corner="authArtwork.corner" :src="authArtwork.src" :width="authArtwork.width" :height="authArtwork.height" decoding="async" alt="" aria-hidden="true">
      <CardHeader class="relative z-10">
        <CardTitle>{{ $t('ui.registrationSuccessful') }}</CardTitle>
        <CardDescription>
          {{ registered.requiresEmailVerification
            ? registered.verificationEmailQueued
              ? $t('ui.theVerificationEmailHasBeenQueuedCheckYourInboxAnd')
              : $t('ui.theVerificationEmailHasNotBeenQueuedYetUseThe')
            : $t('ui.youCanNowLogIn') }}
        </CardDescription>
      </CardHeader>
      <CardContent v-if="registered.requiresEmailVerification" class="relative z-10 space-y-3">
        <Alert v-if="resendDone">
          <AlertDescription>{{ $t('ui.ifThisEmailBelongsToAnUnverifiedAccountAnotherVerification') }}</AlertDescription>
        </Alert>
        <Alert v-if="resendError" variant="destructive">
          <AlertDescription>{{ $message(resendError) }}</AlertDescription>
        </Alert>
        <Button type="button" variant="outline" class="w-full" :disabled="resendPending" @click="resendVerification">
          <Spinner v-if="resendPending" data-icon="inline-start" />
          {{ $t('ui.resendVerificationEmail') }}
        </Button>
      </CardContent>
      <CardFooter class="relative z-10">
        <Button as-child class="w-full">
          <NuxtLink to="/auth/login">{{ $t('ui.goToLogin') }}</NuxtLink>
        </Button>
      </CardFooter>
    </Card>
    <Card v-else class="auth-card">
      <img v-if="authArtwork" data-slot="auth-character-cutout" :data-corner="authArtwork.corner" :src="authArtwork.src" :width="authArtwork.width" :height="authArtwork.height" decoding="async" alt="" aria-hidden="true">
      <CardHeader class="relative z-10 pt-3">
        <CardTitle class="text-xl font-semibold">{{ $t('ui.createAccount') }}</CardTitle>
      </CardHeader>
      <CardContent class="relative z-10">
        <UiForm @submit.prevent="submit">
          <FieldGroup>
            <Alert v-if="error" variant="destructive">
              <AlertDescription>{{ $message(error) }}</AlertDescription>
            </Alert>
            <Field>
              <FieldLabel for="userName">{{ $t('ui.username') }}</FieldLabel>
              <Input id="userName" v-model="userName" autocomplete="username" required class="max-w-none" />
            </Field>
            <Field>
              <FieldLabel for="email">{{ $t('ui.email') }}</FieldLabel>
              <Input id="email" v-model="email" type="email" autocomplete="email" required class="max-w-none" />
            </Field>
            <Field>
              <FieldLabel for="password">{{ $t('ui.password') }}</FieldLabel>
              <PasswordInput id="password" v-model="password" autocomplete="new-password" required />
            </Field>
            <Field>
              <FieldLabel for="confirmPassword">{{ $t('ui.confirmPassword') }}</FieldLabel>
              <PasswordInput id="confirmPassword" v-model="confirmPassword" autocomplete="new-password" required />
            </Field>
            <Field>
              <Button type="submit" :disabled="pending" class="w-full sm:mx-auto sm:w-2/3">
                <Spinner v-if="pending" data-icon="inline-start" />
                {{ $t('ui.createAccount') }}
              </Button>
            </Field>
          </FieldGroup>
        </UiForm>
      </CardContent>
      <CardFooter class="relative z-10 justify-center text-sm text-muted-foreground">
        {{ $t('ui.alreadyHaveAnAccount') }}
        <NuxtLink to="/auth/login" class="ml-1 underline">{{ $t('ui.logInDirectly') }}</NuxtLink>
      </CardFooter>
    </Card>
  </div>
</template>
