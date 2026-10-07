<script setup lang="ts">
import { toRefs } from 'vue'
import type { MfaStepUpViewState } from '~/features/authentication/mfa/useMfaStepUp'
const props = defineProps<{ state: MfaStepUpViewState }>()
const { open, pending, code, method, error, canSubmit, submit, toggleMethod, setOpen } = toRefs(props.state)
</script>
<template>
  <Dialog :open="open" @update:open="setOpen">
    <DialogContent class="max-w-sm"><DialogHeader><DialogTitle>{{ $t('mfa.verify') }}</DialogTitle></DialogHeader>
      <UiForm @submit.prevent="submit"><FieldGroup>
        <Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert>
        <Field><FieldLabel for="mfa-step-code">{{ method === 'Totp' ? $t('mfa.code') : $t('mfa.recoveryCode') }}</FieldLabel>
          <Input id="mfa-step-code" v-model="code" :inputmode="method === 'Totp' ? 'numeric' : 'text'" autocomplete="one-time-code" spellcheck="false" autofocus maxlength="64" required />
        </Field>
        <Button type="submit" :disabled="!canSubmit"><Spinner v-if="pending" data-icon="inline-start" />{{ $t('mfa.confirm') }}</Button>
        <Button type="button" variant="ghost" :disabled="pending" @click="toggleMethod">{{ method === 'Totp' ? $t('mfa.useRecovery') : $t('mfa.useTotp') }}</Button>
      </FieldGroup></UiForm>
    </DialogContent>
  </Dialog>
</template>
