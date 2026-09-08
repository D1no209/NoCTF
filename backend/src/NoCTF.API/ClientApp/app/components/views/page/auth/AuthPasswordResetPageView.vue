<script setup lang="ts">
import { toRefs } from 'vue'
import type { AuthPasswordResetPageViewState } from '~/features/routes/auth/useAuthPasswordResetPage'

const viewProps = defineProps<{ state: AuthPasswordResetPageViewState }>()
const { configuration, token, email, requested, newPassword, confirmPassword, completed, error, pending, requestReset, completeReset } = toRefs(viewProps.state)
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
    <!-- Complete with token -->
    <Card v-if="token">
      <CardHeader>
        <CardTitle>{{ $t('ui.setNewPassword') }}</CardTitle>
        <CardDescription>{{ $t('ui.enterYourNewPasswordToCompleteTheReset') }}</CardDescription>
      </CardHeader>
      <CardContent>
        <Alert v-if="completed" class="mb-4">
          <AlertDescription>{{ $t('ui.thePasswordHasBeenResetPleaseUseTheNewPassword') }}</AlertDescription>
        </Alert>
        <form v-if="!completed" @submit.prevent="completeReset">
          <FieldGroup>
            <Alert v-if="error" variant="destructive">
              <AlertDescription>{{ $message(error) }}</AlertDescription>
            </Alert>
            <Field>
              <FieldLabel for="newPassword">{{ $t('ui.newPassword') }}</FieldLabel>
              <PasswordInput id="newPassword" v-model="newPassword" autocomplete="new-password" required />
            </Field>
            <Field>
              <FieldLabel for="confirmPassword">{{ $t('ui.confirmNewPassword') }}</FieldLabel>
              <PasswordInput id="confirmPassword" v-model="confirmPassword" autocomplete="new-password" required />
            </Field>
            <Field>
              <Button type="submit" :disabled="pending" class="w-full">
                <Spinner v-if="pending" data-icon="inline-start" /> {{ $t('ui.resetPassword') }} </Button>
            </Field>
          </FieldGroup>
        </form>
      </CardContent>
      <CardFooter>
        <Button as-child variant="outline" class="w-full">
          <NuxtLink to="/auth/login">{{ $t('ui.goToLogin') }}</NuxtLink>
        </Button>
      </CardFooter>
    </Card>

    <!-- Request reset email -->
    <Card v-else>
      <CardHeader>
        <CardTitle>{{ $t('ui.resetPassword') }}</CardTitle>
        <CardDescription>{{ $t('ui.enterYourRegisteredEmailAndWeWillSendAReset') }}</CardDescription>
      </CardHeader>
      <CardContent>
        <Alert v-if="requested" class="mb-4">
          <AlertDescription>{{ $t('ui.ifTheEmailAddressHasBeenRegisteredAndTheReset') }}</AlertDescription>
        </Alert>
        <form v-if="!requested" @submit.prevent="requestReset">
          <FieldGroup>
            <Alert v-if="error" variant="destructive">
              <AlertDescription>{{ $message(error) }}</AlertDescription>
            </Alert>
            <Field>
              <FieldLabel for="email">{{ $t('ui.email') }}</FieldLabel>
              <Input id="email" v-model="email" type="email" autocomplete="email" required />
            </Field>
            <Field>
              <Button type="submit" :disabled="pending" class="w-full">
                <Spinner v-if="pending" data-icon="inline-start" /> {{ $t('ui.sendResetEmail') }} </Button>
            </Field>
          </FieldGroup>
        </form>
      </CardContent>
      <CardFooter class="justify-center text-sm text-muted-foreground">
        <NuxtLink to="/auth/login" class="underline">{{ $t('ui.returnToLogin') }}</NuxtLink>
      </CardFooter>
    </Card>
  </div>
</template>
