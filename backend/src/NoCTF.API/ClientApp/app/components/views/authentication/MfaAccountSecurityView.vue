<script setup lang="ts">
import { toRefs } from 'vue'
import type { MfaAccountSecurityViewState } from '~/features/authentication/mfa/useMfaAccountSecurity'
const props = defineProps<{ state: MfaAccountSecurityViewState }>()
const { status, error, loading, stepUp, StepUpView, reauthenticate, enroll, rebind, regenerate, disable } = toRefs(props.state)
</script>
<template><section class="flex flex-col gap-3">
  <h3 class="text-sm font-semibold">{{ $t('mfa.title') }}</h3>
  <Skeleton v-if="loading" class="h-20 w-full" />
  <Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert>
  <template v-if="status">
    <p class="text-sm">{{ status.enrolled ? $t('mfa.enabled') : $t('mfa.disabled') }}</p>
    <p v-if="status.enrolled" class="text-sm text-muted-foreground">{{ $t('mfa.remaining', { count: status.recoveryCodesRemaining ?? 0 }) }}</p>
    <p v-if="!status.recoveryMailAvailable" class="text-sm text-muted-foreground">{{ $t('mfa.mailUnavailable') }}</p>
    <p v-if="status.mandated" class="text-sm text-muted-foreground">{{ $t('mfa.forced') }}</p>
    <template v-if="status.enrolled"><div class="flex flex-wrap gap-2">
      <Button type="button" variant="outline" size="sm" :disabled="stepUp.pending" @click="rebind">{{ $t('mfa.rebind') }}</Button>
      <Button type="button" variant="outline" size="sm" :disabled="stepUp.pending" @click="regenerate">{{ $t('mfa.regenerate') }}</Button>
      <Button v-if="!status.mandated" type="button" variant="destructive" size="sm" :disabled="stepUp.pending" @click="disable">{{ $t('mfa.disable') }}</Button>
    </div></template>
    <Button v-else-if="status.recentPrimaryAuthentication" type="button" variant="outline" size="sm" @click="enroll">{{ $t('mfa.bind') }}</Button>
    <Button v-else type="button" variant="ghost" @click="reauthenticate">{{ $t('mfa.signInAgain') }}</Button>
  </template>
  <component :is="StepUpView" :state="stepUp" />
</section></template>
