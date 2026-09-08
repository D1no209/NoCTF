<script setup lang="ts">
import { toRefs } from 'vue'
import type { AuthVerifyEmailPageViewState } from '~/features/routes/auth/useAuthVerifyEmailPage'

const viewProps = defineProps<{ state: AuthVerifyEmailPageViewState }>()
const { isLoggedIn, configuration, state, message, resendPending, resendDone, email, token, resend } = toRefs(viewProps.state)
</script>

<template>
  <div class="mx-auto flex max-w-md flex-col px-4 py-12">
    <div class="mb-8 flex flex-col items-center gap-2 text-center">
      <NuxtLink to="/" class="flex items-baseline gap-1.5 font-mono text-2xl font-semibold tracking-tight">
        <span class="text-primary">&gt;</span>
        <span>{{ configuration?.name ?? $t('ui.noctf') }}</span>
        <span class="animate-blink text-primary">_</span>
      </NuxtLink>
      <p v-if="configuration?.description" class="text-sm text-muted-foreground">{{ configuration.description }}</p>
    </div>
    <Card>
      <CardHeader>
        <CardTitle>{{ $t('ui.emailVerification') }}</CardTitle>
        <CardDescription v-if="state === 'verifying'">{{ $t('ui.verifyingYourEmail') }}</CardDescription>
        <CardDescription v-else-if="state === 'success'">{{ $t('ui.theEmailVerificationWasSuccessfulAndAllFunctionsCanNow') }}</CardDescription>
        <CardDescription v-else-if="token && state === 'failed'">{{ $t('ui.authenticationFailed') }}</CardDescription>
        <CardDescription v-else>{{ $t('ui.thePlatformRequiresEmailVerificationBeforeYouCanContinueTo') }}</CardDescription>
      </CardHeader>
      <CardContent>
        <FieldGroup>
          <div v-if="state === 'verifying'" class="flex justify-center py-4">
            <Spinner class="size-6" />
          </div>
          <Alert v-if="message" :variant="state === 'failed' ? 'destructive' : 'default'">
            <AlertDescription>{{ message }}</AlertDescription>
          </Alert>
          <Alert v-if="resendDone">
            <AlertDescription>{{ $t('ui.ifThisEmailBelongsToAnUnverifiedAccountAnotherVerification') }}</AlertDescription>
          </Alert>
          <Field v-if="!isLoggedIn && state !== 'success' && !resendDone">
            <FieldLabel for="email">{{ $t('ui.email') }}</FieldLabel>
            <Input id="email" v-model="email" type="email" autocomplete="email" required />
          </Field>
        </FieldGroup>
      </CardContent>
      <CardFooter class="flex flex-col gap-2">
        <Button v-if="state === 'success'" as-child class="w-full">
          <NuxtLink to="/auth/login">{{ $t('ui.goToLogin') }}</NuxtLink>
        </Button>
        <Button
          v-if="state !== 'success' && !resendDone"
          variant="outline"
          class="w-full"
          :disabled="resendPending || (!isLoggedIn && !email)"
          @click="resend"
        >
          <Spinner v-if="resendPending" data-icon="inline-start" /> {{ $t('ui.resendVerificationEmail') }} </Button>
        <Button v-if="!isLoggedIn && state !== 'success'" as-child variant="ghost" class="w-full">
          <NuxtLink to="/auth/login">{{ $t('ui.returnToLogin') }}</NuxtLink>
        </Button>
      </CardFooter>
    </Card>
  </div>
</template>
