<script setup lang="ts">
import { toRefs } from 'vue'
import type { HumanVerificationGateViewState } from '~/features/security/useHumanVerification'

const viewProps = defineProps<{ state: HumanVerificationGateViewState }>()
const { ShieldCheck, TurnstileWidget, open, provider, challengeId, siteKey, progress, errorMessage, turnstileToken, turnstileOptions, setOpen, cancel, retry } = toRefs(viewProps.state)
</script>

<template>
  <Dialog :open="open" @update:open="setOpen">
    <DialogContent :show-close-button="false" class="sm:max-w-md">
      <CardHeader class="pb-1">
        <CardTitle class="flex items-center gap-2 text-lg">
          <component :is="ShieldCheck" class="size-5 text-primary" aria-hidden="true" />
          {{ $t('common.label.completeHumanVerification') }}
        </CardTitle>
        <CardDescription>{{ $t('common.humanVerification.description.completeVerificationSensitive') }}</CardDescription>
      </CardHeader>
      <CardContent class="grid min-h-24 place-items-center gap-3 py-4 text-center">
        <Alert v-if="errorMessage" variant="destructive" class="w-full text-left">
          <AlertDescription>{{ errorMessage }}</AlertDescription>
        </Alert>
        <template v-else-if="provider === 'Cap'">
          <Spinner class="size-6 text-primary" />
          <p class="text-sm text-muted-foreground" role="status" aria-live="polite">
            {{ $t('common.authLogin.label.computingProofWorkProgress', { progress }) }}
          </p>
        </template>
        <div v-else-if="provider === 'Turnstile'" class="grid justify-items-center gap-3">
          <p class="text-sm text-muted-foreground">{{ $t('common.label.preparingHumanVerification') }}</p>
          <component
            :is="TurnstileWidget"
            :key="challengeId"
            v-model="turnstileToken"
            :site-key="siteKey"
            :options="turnstileOptions"
            :reset-interval="240000"
          />
        </div>
        <p v-else class="text-sm text-muted-foreground">{{ $t('common.label.preparingHumanVerification') }}</p>
      </CardContent>
      <CardFooter class="justify-end gap-2">
        <Button type="button" variant="ghost" @click="cancel">{{ $t('common.action.cancel') }}</Button>
        <Button v-if="errorMessage" type="button" @click="retry">{{ $t('common.label.retry') }}</Button>
      </CardFooter>
    </DialogContent>
  </Dialog>
</template>
