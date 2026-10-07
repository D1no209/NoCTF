<script setup lang="ts">
import { toRefs } from 'vue'
import type { MfaPlatformSettingsViewState } from '~/features/authentication/mfa/useMfaPlatformSettings'
const props = defineProps<{ state: MfaPlatformSettingsViewState }>()
const { available, loading, error, policy, policies, providers, providerId, trust, stepUp, StepUpView, savePolicy, saveTrust } = toRefs(props.state)
</script>
<template>
  <Card v-if="available"><CardHeader><CardTitle>{{ $t('mfa.title') }}</CardTitle></CardHeader><CardContent class="flex flex-col gap-6">
    <Skeleton v-if="loading" class="h-24 w-full" />
    <Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert>
    <UiForm v-if="!loading" @submit.prevent="savePolicy"><FieldGroup>
      <Field><FieldLabel for="mfa-policy">{{ $t('mfa.policy') }}</FieldLabel>
        <Select v-model="policy"><SelectTrigger id="mfa-policy"><SelectValue /></SelectTrigger><SelectContent><SelectItem v-for="option in policies" :key="option" :value="option">{{ $t(`mfa.policy.${option}`) }}</SelectItem></SelectContent></Select>
        <FieldDescription>{{ $t('mfa.policyNotice') }}</FieldDescription>
      </Field>
      <Button type="submit" :disabled="stepUp.pending">{{ $t('mfa.save') }}</Button>
    </FieldGroup></UiForm>
    <template v-if="providers.length"><Separator />
      <UiForm @submit.prevent="saveTrust"><FieldGroup>
        <Field><FieldLabel for="mfa-provider">{{ $t('mfa.provider') }}</FieldLabel><Select v-model="providerId"><SelectTrigger id="mfa-provider"><SelectValue /></SelectTrigger><SelectContent><SelectItem v-for="provider in providers" :key="provider.providerId" :value="provider.providerId ?? ''">{{ provider.name }}</SelectItem></SelectContent></Select></Field>
        <Field orientation="horizontal"><Switch id="mfa-trust" v-model="trust.enabled" /><FieldLabel for="mfa-trust">{{ $t('mfa.trust') }}</FieldLabel></Field>
        <Field><FieldLabel for="mfa-acr">{{ $t('mfa.acr') }}</FieldLabel><Textarea id="mfa-acr" v-model="trust.acr" rows="3" /></Field>
        <Field><FieldLabel for="mfa-amr">{{ $t('mfa.amr') }}</FieldLabel><Textarea id="mfa-amr" v-model="trust.amr" rows="3" /><FieldDescription>{{ $t('mfa.rulesHint') }}</FieldDescription></Field>
        <Field><FieldLabel for="mfa-max-age">{{ $t('mfa.maxAge') }}</FieldLabel><NumberInput id="mfa-max-age" v-model="trust.maxAgeSeconds" :min="1" :max="300" required /></Field>
        <Button type="submit" :disabled="stepUp.pending || !providerId">{{ $t('mfa.save') }}</Button>
      </FieldGroup></UiForm>
    </template>
    <component :is="StepUpView" :state="stepUp" />
  </CardContent></Card>
</template>
