<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminPlatformEmailPageViewState } from '~/features/routes/admin/platform/useAdminPlatformEmailPage'

const viewProps = defineProps<{ state: AdminPlatformEmailPageViewState }>()
const { KeyRound, Send, configuration, loading, loadError, form, saving, passwordOpen, newPassword, passwordSaving, sendingTest, save, replacePassword, sendTest, AdminDateTime, onClickPasswordOpen, onClickPasswordOpen2 } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-6">
    <Alert v-if="loadError" variant="destructive">
      <AlertDescription>{{ $message(loadError) }}</AlertDescription>
    </Alert>

    <Skeleton v-if="loading" class="h-96 w-full" />

    <template v-else-if="configuration">
      <Card>
        <CardHeader class="flex flex-row items-center justify-between gap-4">
          <div>
            <CardTitle>{{ $t('ui.emailVerificationConfiguration') }}</CardTitle>
            <CardDescription>
              {{ $t('ui.registrationVerificationAndPasswordResetEmailUpdatedAt') }}
              <component :is="AdminDateTime" :value="configuration.updatedAt" />
            </CardDescription>
          </div>
          <div class="flex items-center gap-2">
            <Switch id="email-enabled" v-model="form.enabled" />
            <Label for="email-enabled">{{ $t('ui.enableEmailSending') }}</Label>
          </div>
        </CardHeader>
        <CardContent>
          <form @submit.prevent="save">
            <FieldGroup>
              <Field>
                <FieldLabel for="public-base-url">{{ $t('ui.publiclyAccessibleAddress') }}</FieldLabel>
                <Input id="public-base-url" v-model="form.publicBaseUrl" required :placeholder="$t('ui.httpsCtfExampleCom')" />
                <FieldDescription>{{ $t('ui.usedToGenerateVerificationResetLinksInEmails') }}</FieldDescription>
              </Field>
              <div class="grid gap-4 sm:grid-cols-3">
                <Field>
                  <FieldLabel for="token-lifetime">{{ $t('ui.verificationTokenValidityPeriodMinutes') }}</FieldLabel>
                  <Input id="token-lifetime" v-model.number="form.tokenLifetimeMinutes" type="number" min="1" required />
                </Field>
                <Field>
                  <FieldLabel for="resend-cooldown">{{ $t('ui.resendCooldownSeconds') }}</FieldLabel>
                  <Input id="resend-cooldown" v-model.number="form.resendCooldownSeconds" type="number" min="0" required />
                </Field>
                <Field>
                  <FieldLabel for="smtp-timeout">{{ $t('ui.smtpTimeoutSeconds') }}</FieldLabel>
                  <Input id="smtp-timeout" v-model.number="form.smtpTimeoutSeconds" type="number" min="1" required />
                </Field>
              </div>
              <div class="grid gap-4 sm:grid-cols-3">
                <Field>
                  <FieldLabel for="reset-lifetime">{{ $t('ui.resetTokenValidityPeriodMinutes') }}</FieldLabel>
                  <Input id="reset-lifetime" v-model.number="form.passwordResetTokenLifetimeMinutes" type="number" min="1" required />
                </Field>
                <Field>
                  <FieldLabel for="reset-cooldown">{{ $t('ui.resetCooldownSeconds') }}</FieldLabel>
                  <Input id="reset-cooldown" v-model.number="form.passwordResetCooldownSeconds" type="number" min="0" required />
                </Field>
                <Field>
                  <FieldLabel for="reset-max">{{ $t('ui.resetFrequencyUpperLimitTimesHour') }}</FieldLabel>
                  <Input id="reset-max" v-model.number="form.passwordResetMaxRequestsPerHour" type="number" min="1" required />
                </Field>
              </div>

              <FieldSeparator>{{ $t('ui.smtpServer') }}</FieldSeparator>

              <div class="grid gap-4 sm:grid-cols-3">
                <Field class="sm:col-span-2">
                  <FieldLabel for="smtp-host">{{ $t('ui.host') }}</FieldLabel>
                  <Input id="smtp-host" v-model="form.smtpHost" required :placeholder="$t('ui.smtpExampleCom')" />
                </Field>
                <Field>
                  <FieldLabel for="smtp-port">{{ $t('ui.port') }}</FieldLabel>
                  <Input id="smtp-port" v-model.number="form.smtpPort" type="number" min="1" max="65535" required />
                </Field>
              </div>
              <div class="grid gap-4 sm:grid-cols-3">
                <Field>
                  <FieldLabel for="smtp-security">{{ $t('ui.encryptionMethod') }}</FieldLabel>
                  <Select v-model="form.smtpSecurityMode">
                    <SelectTrigger id="smtp-security" class="w-full">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectGroup>
                        <SelectItem value="None">{{ $t('ui.noEncryption') }}</SelectItem>
                        <SelectItem value="SslOnConnect">{{ $t('ui.sslTls') }}</SelectItem>
                        <SelectItem value="StartTls">{{ $t('ui.starttls') }}</SelectItem>
                      </SelectGroup>
                    </SelectContent>
                  </Select>
                </Field>
                <Field class="sm:col-span-2">
                  <FieldLabel for="smtp-username">{{ $t('ui.username') }}</FieldLabel>
                  <Input id="smtp-username" v-model="form.smtpUserName" required autocomplete="off" />
                </Field>
              </div>
              <div class="grid gap-4 sm:grid-cols-2">
                <Field>
                  <FieldLabel for="smtp-from-address">{{ $t('ui.shippingAddress') }}</FieldLabel>
                  <Input id="smtp-from-address" v-model="form.smtpFromAddress" type="email" required :placeholder="$t('ui.noreplyExampleCom')" />
                </Field>
                <Field>
                  <FieldLabel for="smtp-from-name">{{ $t('ui.senderName') }}</FieldLabel>
                  <Input id="smtp-from-name" v-model="form.smtpFromName" required :placeholder="$t('ui.noctf')" />
                </Field>
              </div>
              <Field orientation="horizontal" class="flex-wrap gap-4">
                <Button type="submit" :disabled="saving">
                  <Spinner v-if="saving" data-icon="inline-start" /> {{ $t('ui.saveConfiguration') }} </Button>
                <Button type="button" variant="outline" @click="onClickPasswordOpen(true)">
                  <KeyRound data-icon="inline-start" /> {{ $t('ui.changeSmtpPassword') }} <Badge :variant="configuration.smtpPasswordConfigured ? 'secondary' : 'destructive'" class="ml-2">
                    {{ configuration.smtpPasswordConfigured ? $t('ui.configured') : $t('ui.notConfigured') }}
                  </Badge>
                </Button>
                <Button type="button" variant="outline" :disabled="sendingTest || !form.enabled" @click="sendTest">
                  <Spinner v-if="sendingTest" data-icon="inline-start" />
                  <Send v-else data-icon="inline-start" /> {{ $t('ui.sendTestEmail') }} </Button>
              </Field>
              <p class="text-sm text-muted-foreground"> {{ $t('ui.theTestEmailWillBeSentToTheEmailAddress') }} </p>
            </FieldGroup>
          </form>
        </CardContent>
      </Card>
    </template>

    <Dialog v-model:open="passwordOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ $t('ui.changeSmtpPassword') }}</DialogTitle>
          <DialogDescription>{{ $t('ui.thePasswordIsOnlyUsedForSmtpAuthenticationAndWill') }}</DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel for="smtp-password">{{ $t('ui.newPassword') }}</FieldLabel>
            <PasswordInput id="smtp-password" v-model="newPassword" required autocomplete="new-password" />
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" @click="onClickPasswordOpen2(false)">{{ $t('ui.cancel') }}</Button>
          <Button :disabled="passwordSaving || !newPassword" @click="replacePassword">
            <Spinner v-if="passwordSaving" data-icon="inline-start" /> {{ $t('ui.savePassword') }} </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
