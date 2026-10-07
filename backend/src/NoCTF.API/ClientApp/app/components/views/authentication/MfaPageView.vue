<script setup lang="ts">
import { toRefs } from 'vue'
import type { MfaPageViewState } from '~/features/authentication/mfa/useMfaPage'
const props = defineProps<{ state: MfaPageViewState }>()
const { flow, loading, pending, error, code, method, recoveryCodes, copied, enrollment, expired, canSubmit, submit, toggleMethod, copySecret, copyCodes, cancel, finish, primaryLogin } = toRefs(props.state)
</script>
<template>
  <main class="mx-auto w-full max-w-xl px-4 py-12">
    <Card>
      <CardHeader><CardTitle>{{ recoveryCodes.length ? $t('mfa.saveCodes') : enrollment ? $t('mfa.enroll') : $t('mfa.verify') }}</CardTitle></CardHeader>
      <CardContent class="flex flex-col gap-5">
        <Skeleton v-if="loading" class="h-40 w-full" />
        <Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert>
        <template v-if="recoveryCodes.length">
          <p class="text-sm text-muted-foreground">{{ $t('mfa.codesNotice') }}</p>
          <div class="grid grid-cols-1 gap-2 rounded-lg bg-muted p-4 font-mono text-xs sm:text-sm"><code v-for="item in recoveryCodes" :key="item" class="whitespace-nowrap">{{ item }}</code></div>
          <Button type="button" variant="outline" @click="copyCodes">{{ copied ? $t('mfa.copied') : $t('mfa.copy') }}</Button>
          <Button type="button" @click="finish">{{ $t('mfa.saved') }}</Button>
        </template>
        <template v-else-if="flow && !loading && !expired">
          <template v-if="flow.primaryAuthenticationRequired">
            <p>{{ $t('mfa.primaryRequired') }}</p><Button type="button" @click="primaryLogin">{{ $t('auth.login.action') }}</Button>
          </template>
          <UiForm v-else @submit.prevent="submit">
            <FieldGroup>
              <template v-if="enrollment && flow.secret">
                <p class="text-sm text-muted-foreground">{{ $t('mfa.scan') }}</p>
                <p v-if="!flow.recoveryMailAvailable" class="text-sm text-muted-foreground">{{ $t('mfa.mailUnavailable') }}</p>
                <QrCode v-if="flow.provisioningUri" :value="flow.provisioningUri" :label="$t('mfa.qr')" />
                <Field><FieldLabel>{{ $t('mfa.secret') }}</FieldLabel><code class="break-all rounded-lg bg-muted p-3 font-mono">{{ flow.secret }}</code>
                  <Button type="button" variant="outline" @click="copySecret">{{ copied ? $t('mfa.copied') : $t('mfa.copy') }}</Button>
                </Field>
              </template>
              <Field><FieldLabel for="mfa-code">{{ method === 'Totp' ? $t('mfa.code') : $t('mfa.recoveryCode') }}</FieldLabel>
                <Input id="mfa-code" v-model="code" :inputmode="method === 'Totp' ? 'numeric' : 'text'" autocomplete="one-time-code" autocapitalize="off" spellcheck="false" autofocus :maxlength="method === 'Totp' ? 12 : 64" required />
              </Field>
              <Button type="submit" :disabled="!canSubmit"><Spinner v-if="pending" data-icon="inline-start" />{{ $t('mfa.confirm') }}</Button>
              <Button v-if="!enrollment && flow.recoveryAvailable" type="button" variant="ghost" :disabled="pending" @click="toggleMethod">{{ method === 'Totp' ? $t('mfa.useRecovery') : $t('mfa.useTotp') }}</Button>
            </FieldGroup>
          </UiForm>
        </template>
        <p v-else-if="expired" class="text-sm">{{ $t('mfa.error.FlowExpired') }}</p>
        <Button v-if="!recoveryCodes.length" type="button" variant="ghost" :disabled="pending" @click="cancel">{{ expired ? $t('mfa.retry') : $t('mfa.cancel') }}</Button>
      </CardContent>
    </Card>
  </main>
</template>
