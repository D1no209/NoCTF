<script setup lang="ts">
import { toRefs } from 'vue'
import type { AuthLoginPageViewState } from '~/features/routes/auth/useAuthLoginPage'

const viewProps = defineProps<{ state: AuthLoginPageViewState }>()
const { authArtwork, configuration, loginName, password, error, pending, submit } = toRefs(viewProps.state)
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
    <Card class="auth-card">
      <img v-if="authArtwork" data-slot="auth-character-cutout" :data-corner="authArtwork.corner" :src="authArtwork.src" :width="authArtwork.width" :height="authArtwork.height" decoding="async" alt="" aria-hidden="true">
      <CardHeader class="relative z-10 pt-3">
        <CardTitle class="text-xl font-semibold">{{ $t('ui.signIn') }}</CardTitle>
      </CardHeader>
      <CardContent class="relative z-10">
        <UiForm @submit.prevent="submit">
          <FieldGroup>
            <Alert v-if="error" variant="destructive">
              <AlertDescription>{{ $message(error) }}</AlertDescription>
            </Alert>
            <Field>
              <FieldLabel for="login">{{ $t('ui.usernameOrEmail') }}</FieldLabel>
              <Input id="login" v-model="loginName" autocomplete="username" required class="max-w-none" />
            </Field>
            <Field>
              <FieldLabel for="password">{{ $t('ui.password') }}</FieldLabel>
              <PasswordInput id="password" v-model="password" autocomplete="current-password" required />
              <FieldDescription>
                <NuxtLink to="/auth/password-reset" class="underline">{{ $t('ui.forgotYourPassword') }}</NuxtLink>
              </FieldDescription>
            </Field>
            <Field>
              <Button type="submit" :disabled="pending" class="w-full sm:mx-auto sm:w-2/3">
                <Spinner v-if="pending" data-icon="inline-start" />
                {{ $t('ui.signIn') }}
              </Button>
            </Field>
          </FieldGroup>
        </UiForm>
      </CardContent>
      <CardFooter class="relative z-10 justify-center text-sm text-muted-foreground">
        {{ $t('ui.donTHaveAnAccountYet') }}
        <NuxtLink to="/auth/register" class="ml-1 underline">{{ $t('ui.registerNow') }}</NuxtLink>
      </CardFooter>
    </Card>
  </div>
</template>

<style src="./auth-artwork.css"></style>
