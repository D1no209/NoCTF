<script setup lang="ts">
import { toRefs } from 'vue'
import type { PasskeyAccountSecurityViewState } from '~/features/authentication/passkeys/usePasskeyAccountSecurity'
const props = defineProps<{ state: PasskeyAccountSecurityViewState }>()
const { status, loading, pending, error, dialog, name, selected, canAdd, supported, stepUp, StepUpView, openAdd, openRename, openRemove, setDialogOpen, submitName, confirmRemove, signInAgain } = toRefs(props.state)
</script>
<template>
  <section class="flex flex-col gap-3">
    <h3 class="text-sm font-semibold">{{ $t('passkeys.title') }}</h3>
    <Skeleton v-if="loading" class="h-20 w-full" />
    <Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert>
    <template v-if="status">
      <p class="text-sm text-muted-foreground">{{ $t('passkeys.fallback') }}</p>
      <p v-if="!status.available || !supported" class="text-sm text-muted-foreground">{{ $t('passkeys.unavailable') }}</p>
      <p v-if="!status.credentials?.length" class="text-sm text-muted-foreground">{{ $t('passkeys.empty') }}</p>
      <div v-for="credential in status.credentials" :key="credential.id" class="flex flex-wrap items-center justify-between gap-2 rounded-lg bg-muted p-3">
        <div class="min-w-0"><p class="break-words text-sm font-medium">{{ credential.name }}</p><p class="text-xs text-muted-foreground">{{ credential.isBackedUp ? $t('passkeys.backedUp') : $t('passkeys.deviceBound') }}</p></div>
        <div class="flex gap-2"><Button type="button" size="sm" variant="ghost" :disabled="pending || stepUp.pending" @click="openRename(credential)">{{ $t('passkeys.rename') }}</Button><Button type="button" size="sm" variant="ghost" :disabled="pending || stepUp.pending" @click="openRemove(credential)">{{ $t('passkeys.remove') }}</Button></div>
      </div>
      <Button v-if="status.recentPrimaryAuthentication || status.needsLocalProof" type="button" size="sm" variant="outline" class="self-start" :disabled="!canAdd || pending || stepUp.pending" @click="openAdd"><Spinner v-if="pending" data-icon="inline-start" />{{ $t('passkeys.add') }}</Button>
      <Button v-else type="button" variant="ghost" @click="signInAgain">{{ $t('mfa.signInAgain') }}</Button>
    </template>
    <Dialog :open="dialog !== null" @update:open="setDialogOpen"><DialogContent class="max-w-sm">
      <DialogHeader><DialogTitle>{{ dialog === 'remove' ? $t('passkeys.removeTitle') : dialog === 'rename' ? $t('passkeys.renameTitle') : $t('passkeys.add') }}</DialogTitle><DialogDescription v-if="dialog === 'remove'">{{ $t('passkeys.removeDescription') }}</DialogDescription></DialogHeader>
      <template v-if="dialog === 'remove'"><p class="break-all text-sm">{{ selected?.name }}</p><DialogFooter><Button type="button" variant="destructive" :disabled="pending" @click="confirmRemove">{{ $t('passkeys.remove') }}</Button></DialogFooter></template>
      <UiForm v-else @submit.prevent="submitName"><FieldGroup><Field><FieldLabel for="passkey-name">{{ $t('passkeys.name') }}</FieldLabel><Input id="passkey-name" v-model="name" maxlength="64" required autofocus /></Field><Button type="submit" :disabled="pending || !name.trim()">{{ $t('mfa.save') }}</Button></FieldGroup></UiForm>
    </DialogContent></Dialog>
    <component :is="StepUpView" :state="stepUp" />
  </section>
</template>
