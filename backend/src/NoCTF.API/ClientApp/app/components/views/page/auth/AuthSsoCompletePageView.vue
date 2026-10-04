<script setup lang="ts">
import { toRefs } from 'vue'
import type { AuthSsoCompletePageViewState } from '~/features/routes/auth/useAuthSsoCompletePage'

const viewProps = defineProps<{ state: AuthSsoCompletePageViewState }>()
const { flow, loading, pending, error, identityNotLinked, bindingLoginTarget, bindingRegisterTarget, completeLogin, completeBinding } = toRefs(viewProps.state)
</script>

<template>
  <div class="mx-auto flex min-h-[60vh] w-full max-w-lg items-center px-4 py-12">
    <Card class="w-full">
      <CardHeader>
        <CardTitle>{{ $t('sso.completingAuthentication') }}</CardTitle>
        <CardDescription v-if="flow?.providerName">{{ flow.providerName }}</CardDescription>
      </CardHeader>
      <CardContent class="flex flex-col gap-5">
        <div v-if="loading || pending" class="flex items-center gap-3 text-sm text-muted-foreground">
          <Spinner />
          {{ $t('sso.verifyingIdentity') }}
        </div>
        <Alert v-else-if="error" variant="destructive">
          <AlertDescription>{{ $message(error) }}</AlertDescription>
        </Alert>
        <template v-else-if="identityNotLinked">
          <Alert>
            <AlertDescription>{{ $t('sso.identityNotLinked') }}</AlertDescription>
          </Alert>
          <div class="flex flex-col gap-3 sm:flex-row">
            <Button as-child class="flex-1">
              <NuxtLink :to="bindingLoginTarget">{{ $t('sso.signInToBind') }}</NuxtLink>
            </Button>
            <Button as-child variant="outline" class="flex-1">
              <NuxtLink :to="bindingRegisterTarget">{{ $t('sso.registerToBind') }}</NuxtLink>
            </Button>
          </div>
        </template>
        <template v-else-if="flow?.state === 'Authenticated' && flow.intent === 'Bind'">
          <div class="rounded-xl border p-4">
            <p class="font-medium">{{ flow.displayName || flow.subject }}</p>
            <p class="mt-1 break-all text-xs text-muted-foreground">{{ flow.subject }}</p>
          </div>
          <Button :disabled="pending" @click="completeBinding">
            <Spinner v-if="pending" data-icon="inline-start" />
            {{ $t('sso.confirmBinding') }}
          </Button>
        </template>
        <template v-else-if="flow?.state === 'Authenticated' && flow.intent === 'AdministratorTest'">
          <Alert><AlertDescription>{{ $t('sso.authenticationTestSuccessful') }}</AlertDescription></Alert>
          <div class="rounded-xl border p-4">
            <p class="font-medium">{{ flow.displayName || flow.subject }}</p>
            <p class="mt-1 break-all text-xs text-muted-foreground">{{ flow.subject }}</p>
          </div>
          <Button as-child variant="outline"><NuxtLink to="/admin/platform/authentication">{{ $t('sso.returnToAuthenticationSettings') }}</NuxtLink></Button>
        </template>
        <div v-else class="flex flex-col gap-3">
          <Button v-if="flow?.intent === 'Login'" :disabled="pending" @click="completeLogin">{{ $t('sso.retryCompletion') }}</Button>
          <Button as-child variant="outline"><NuxtLink to="/auth/login">{{ $t('account.label.backLogin') }}</NuxtLink></Button>
        </div>
      </CardContent>
      <CardFooter v-if="error && !identityNotLinked" class="justify-center gap-3 text-sm">
        <NuxtLink to="/auth/login" class="underline">{{ $t('auth.login.action') }}</NuxtLink>
        <NuxtLink to="/auth/register" class="underline">{{ $t('common.label.registerNow') }}</NuxtLink>
      </CardFooter>
    </Card>
  </div>
</template>
