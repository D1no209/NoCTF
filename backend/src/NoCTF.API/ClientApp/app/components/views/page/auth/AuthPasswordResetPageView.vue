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
        <span>{{ configuration?.name ?? $t('common.label.noctf') }}</span>
        <span class="animate-blink text-primary">_</span>
      </NuxtLink>
      <p v-if="configuration?.description" class="text-sm text-muted-foreground">{{ configuration.description }}</p>
    </div>
    <!-- Complete with token -->
    <Card v-if="token">
      <CardHeader>
        <CardTitle>{{ $t('common.label.setNewPassword') }}</CardTitle>
        <CardDescription>{{ $t('common.authPassword.description.enterNewPasswordComplete') }}</CardDescription>
      </CardHeader>
      <CardContent>
        <Alert v-if="completed" class="mb-4">
          <AlertDescription>{{ $t('common.authPassword.description.passwordResetNewPassword') }}</AlertDescription>
        </Alert>
        <UiForm v-if="!completed" @submit.prevent="completeReset">
          <FieldGroup>
            <Alert v-if="error" variant="destructive">
              <AlertDescription>{{ $message(error) }}</AlertDescription>
            </Alert>
            <Field>
              <FieldLabel for="newPassword">{{ $t('common.label.newPassword') }}</FieldLabel>
              <PasswordInput id="newPassword" v-model="newPassword" autocomplete="new-password" required />
            </Field>
            <Field>
              <FieldLabel for="confirmPassword">{{ $t('common.label.confirmNewPassword') }}</FieldLabel>
              <PasswordInput id="confirmPassword" v-model="confirmPassword" autocomplete="new-password" required />
            </Field>
            <Field>
              <Button type="submit" :disabled="pending" class="w-full">
                <Spinner v-if="pending" data-icon="inline-start" /> {{ $t('common.label.resetPassword') }} </Button>
            </Field>
          </FieldGroup>
        </UiForm>
      </CardContent>
      <CardFooter>
        <Button as-child variant="outline" class="w-full">
          <NuxtLink to="/auth/login">{{ $t('common.label.goLogin') }}</NuxtLink>
        </Button>
      </CardFooter>
    </Card>

    <!-- Request reset email -->
    <Card v-else>
      <CardHeader>
        <CardTitle>{{ $t('common.label.resetPassword') }}</CardTitle>
        <CardDescription>{{ $t('common.authPassword.description.enterRegisteredEmailWe') }}</CardDescription>
      </CardHeader>
      <CardContent>
        <Alert v-if="requested" class="mb-4">
          <AlertDescription>{{ $t('common.authPassword.description.emailAddressRegisteredReset') }}</AlertDescription>
        </Alert>
        <UiForm v-if="!requested" @submit.prevent="requestReset">
          <FieldGroup>
            <Alert v-if="error" variant="destructive">
              <AlertDescription>{{ $message(error) }}</AlertDescription>
            </Alert>
            <Field>
              <FieldLabel for="email">{{ $t('common.label.email') }}</FieldLabel>
              <Input id="email" v-model="email" type="email" autocomplete="email" required />
            </Field>
            <Field>
              <Button type="submit" :disabled="pending" class="w-full">
                <Spinner v-if="pending" data-icon="inline-start" /> {{ $t('common.label.sendResetEmail') }} </Button>
            </Field>
          </FieldGroup>
        </UiForm>
      </CardContent>
      <CardFooter class="justify-center text-sm text-muted-foreground">
        <NuxtLink to="/auth/login" class="underline">{{ $t('common.label.returnLogin') }}</NuxtLink>
      </CardFooter>
    </Card>
  </div>
</template>
