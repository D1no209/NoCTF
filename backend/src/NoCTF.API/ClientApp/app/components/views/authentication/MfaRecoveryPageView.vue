<script setup lang="ts">
import { toRefs } from 'vue'
import type { MfaRecoveryPageViewState } from '~/features/authentication/mfa/useMfaRecoveryPage'
const props = defineProps<{ state: MfaRecoveryPageViewState }>()
const { loading, error } = toRefs(props.state)
</script>
<template><main class="mx-auto w-full max-w-xl px-4 py-12"><Card><CardHeader><CardTitle>{{ $t('mfa.recoveryTitle') }}</CardTitle></CardHeader><CardContent class="flex flex-col gap-4"><p v-if="loading">{{ $t('mfa.recovering') }}</p><Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert><NuxtLink v-if="error" to="/auth/login">{{ $t('auth.login.action') }}</NuxtLink></CardContent></Card></main></template>
