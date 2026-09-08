<script setup lang="ts">
import { toRefs } from 'vue'
import type { AuthRegisterPageViewState } from '~/features/routes/auth/useAuthRegisterPage'

const viewProps = defineProps<{ state: AuthRegisterPageViewState }>()
const { configuration, userName, email, password, confirmPassword, error, pending, registered, resendPending, resendDone, resendError, submit, resendVerification } = toRefs(viewProps.state)
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
    <Card v-if="registered">
      <CardHeader>
        <CardTitle>{{ $t('ui.registrationSuccessful') }}</CardTitle>
        <CardDescription>
          {{ registered.requiresEmailVerification
            ? registered.verificationEmailQueued
              ? $t('ui.theVerificationEmailHasBeenQueuedCheckYourInboxAnd')
              : $t('ui.theVerificationEmailHasNotBeenQueuedYetUseThe')
            : $t('ui.youCanNowLogIn') }}
        </CardDescription>
      </CardHeader>
      <CardContent v-if="registered.requiresEmailVerification" class="space-y-3">
        <Alert v-if="resendDone">
          <AlertDescription>{{ $t('ui.ifThisEmailBelongsToAnUnverifiedAccountAnotherVerification') }}</AlertDescription>
        </Alert>
        <Alert v-if="resendError" variant="destructive">
          <AlertDescription>{{ $message(resendError) }}</AlertDescription>
        </Alert>
        <Button
          type="button"
          variant="outline"
          class="w-full"
          :disabled="resendPending"
          @click="resendVerification"
        >
          <Spinner v-if="resendPending" data-icon="inline-start" />
          {{ $t('ui.resendVerificationEmail') }}
        </Button>
      </CardContent>
      <CardFooter>
        <Button as-child class="w-full">
          <NuxtLink to="/auth/login">{{ $t('ui.goToLogin') }}</NuxtLink>
        </Button>
      </CardFooter>
    </Card>
    <Card v-else>
      <CardHeader>
        <CardTitle>{{ $t('ui.createAccount') }}</CardTitle>
        <CardDescription>{{ $t('ui.createANewAccountToEnterTheContest') }}</CardDescription>
      </CardHeader>
      <CardContent>
        <form @submit.prevent="submit">
          <FieldGroup>
            <Alert v-if="error" variant="destructive">
              <AlertDescription>{{ $message(error) }}</AlertDescription>
            </Alert>
            <Field>
              <FieldLabel for="userName">{{ $t('ui.username') }}</FieldLabel>
              <Input id="userName" v-model="userName" autocomplete="username" required />
            </Field>
            <Field>
              <FieldLabel for="email">{{ $t('ui.email') }}</FieldLabel>
              <Input id="email" v-model="email" type="email" autocomplete="email" required />
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
              <Button type="submit" :disabled="pending" class="w-full">
                <Spinner v-if="pending" data-icon="inline-start" /> {{ $t('ui.createAccount') }} </Button>
            </Field>
          </FieldGroup>
        </form>
      </CardContent>
      <CardFooter class="justify-center text-sm text-muted-foreground"> {{ $t('ui.alreadyHaveAnAccount') }} <NuxtLink to="/auth/login" class="ml-1 underline">{{ $t('ui.logInDirectly') }}</NuxtLink>
      </CardFooter>
    </Card>
  </div>
</template>
