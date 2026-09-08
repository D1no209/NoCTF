<script setup lang="ts">
import { toRefs } from 'vue'
import type { AuthLoginPageViewState } from '~/features/routes/auth/useAuthLoginPage'

const viewProps = defineProps<{ state: AuthLoginPageViewState }>()
const { login, configuration, loginName, password, error, pending, submit } = toRefs(viewProps.state)
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
        <CardTitle>{{ $t('ui.signIn') }}</CardTitle>
        <CardDescription>{{ $t('ui.logInToYourAccountUsingYourUsernameOrEmail') }}</CardDescription>
      </CardHeader>
      <CardContent>
        <form @submit.prevent="submit">
          <FieldGroup>
            <Alert v-if="error" variant="destructive">
              <AlertDescription>{{ $message(error) }}</AlertDescription>
            </Alert>
            <Field>
              <FieldLabel for="login">{{ $t('ui.usernameOrEmail') }}</FieldLabel>
              <Input id="login" v-model="loginName" autocomplete="username" required />
            </Field>
            <Field>
              <FieldLabel for="password">{{ $t('ui.password') }}</FieldLabel>
              <PasswordInput id="password" v-model="password" autocomplete="current-password" required />
              <FieldDescription>
                <NuxtLink to="/auth/password-reset" class="underline">{{ $t('ui.forgotYourPassword') }}</NuxtLink>
              </FieldDescription>
            </Field>
            <Field>
              <Button type="submit" :disabled="pending" class="w-full">
                <Spinner v-if="pending" data-icon="inline-start" /> {{ $t('ui.signIn') }} </Button>
            </Field>
          </FieldGroup>
        </form>
      </CardContent>
      <CardFooter class="justify-center text-sm text-muted-foreground"> {{ $t('ui.donTHaveAnAccountYet') }} <NuxtLink to="/auth/register" class="ml-1 underline">{{ $t('ui.registerNow') }}</NuxtLink>
      </CardFooter>
    </Card>
  </div>
</template>
