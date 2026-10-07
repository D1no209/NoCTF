<script setup lang="ts">
import { toRefs } from 'vue'
import type { MfaUserManagementViewState } from '~/features/authentication/mfa/useMfaUserManagement'
const props = defineProps<{ state: MfaUserManagementViewState }>()
const { available, required, reason, error, stepUp, StepUpView, saveRequirement, grantRecovery } = toRefs(props.state)
</script>
<template><section v-if="available" class="flex flex-col gap-4">
  <h3 class="text-sm font-semibold">{{ $t('mfa.title') }}</h3>
  <Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert>
  <UiForm @submit.prevent="saveRequirement"><FieldGroup><Field orientation="horizontal"><Switch id="mfa-user-required" v-model="required" /><FieldLabel for="mfa-user-required">{{ $t('mfa.requireAccount') }}</FieldLabel></Field><Button type="submit" variant="outline" :disabled="stepUp.pending">{{ $t('mfa.save') }}</Button></FieldGroup></UiForm>
  <UiForm @submit.prevent="grantRecovery"><FieldGroup><Field><FieldLabel for="mfa-recovery-reason">{{ $t('mfa.reason') }}</FieldLabel><Textarea id="mfa-recovery-reason" v-model="reason" maxlength="1024" required /><FieldDescription>{{ $t('mfa.grantNotice') }}</FieldDescription></Field><Button type="submit" :disabled="stepUp.pending || !reason.trim()">{{ $t('mfa.recoveryGrant') }}</Button></FieldGroup></UiForm>
  <component :is="StepUpView" :state="stepUp" />
</section></template>
