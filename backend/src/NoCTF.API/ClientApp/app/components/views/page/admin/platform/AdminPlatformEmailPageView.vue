<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminPlatformEmailPageViewState } from '~/features/routes/admin/platform/useAdminPlatformEmailPage'

const viewProps = defineProps<{ state: AdminPlatformEmailPageViewState }>()
const { KeyRound, RefreshCw, Send, ShieldCheck, configuration, humanVerification, humanForm, humanVerificationSaving, humanVerificationDirty, humanVerificationReady, humanVerificationProviderLabel, selectedSecretConfigured, humanSecretOpen, humanSecret, humanSecretSaving, loading, loadError, form, emailDirty, saving, passwordOpen, newPassword, passwordSaving, sendingTest, load, saveHumanVerification, replaceHumanVerificationSecret, save, replacePassword, sendTest, AdminDateTime, onClickPasswordOpen, onClickPasswordOpen2, setHumanSecretOpen } = toRefs(viewProps.state)
</script>

<template>
  <section class="flex min-w-0 flex-col gap-6">
    <Alert v-if="loadError" variant="destructive">
      <AlertDescription>{{ $message(loadError) }}</AlertDescription>
    </Alert>

    <template v-if="loading">
      <Skeleton class="h-44 w-full" />
      <Skeleton class="h-96 w-full" />
    </template>

    <Card v-else-if="!configuration">
      <CardHeader>
        <CardTitle>{{ $t('ui.emailAndHumanVerification') }}</CardTitle>
        <CardDescription>{{ $t('ui.platformConfigurationCouldNotBeLoaded') }}</CardDescription>
      </CardHeader>
      <CardContent>
        <Button variant="outline" @click="load">
          <RefreshCw data-icon="inline-start" />
          {{ $t('ui.retry') }}
        </Button>
      </CardContent>
    </Card>

    <template v-else-if="configuration">
      <Card v-if="humanVerification">
        <CardHeader class="flex flex-row items-start justify-between gap-4">
          <div class="flex min-w-0 flex-col gap-1">
            <CardTitle class="flex items-center gap-2">
              <ShieldCheck class="size-5" />
              {{ $t('ui.humanVerificationConfiguration') }}
            </CardTitle>
            <CardDescription>{{ $t('ui.humanVerificationAdminDescription') }}</CardDescription>
          </div>
          <Badge :variant="humanVerificationReady ? 'secondary' : 'outline'">
            {{ humanVerificationReady ? $t('ui.configurationComplete') : $t('ui.configurationIncomplete') }}
          </Badge>
        </CardHeader>
        <CardContent>
          <UiForm class="flex flex-col gap-5" @submit.prevent="saveHumanVerification">
            <FieldGroup>
              <Field>
                <FieldLabel for="human-verification-provider">{{ $t('ui.provider') }}</FieldLabel>
                <Select v-model="humanForm.provider" :disabled="humanVerificationSaving">
                  <SelectTrigger id="human-verification-provider" class="w-full">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectGroup>
                      <SelectItem value="None">{{ $t('ui.disabled') }}</SelectItem>
                      <SelectItem value="Cap">{{ $t('ui.capProvider') }}</SelectItem>
                      <SelectItem value="Turnstile">{{ $t('ui.turnstileProvider') }}</SelectItem>
                    </SelectGroup>
                  </SelectContent>
                </Select>
                <FieldDescription>{{ $t('ui.humanVerificationProviderDescription') }}</FieldDescription>
              </Field>
              <Field orientation="horizontal">
                <Switch
                  id="human-verification-enabled"
                  v-model="humanForm.enabled"
                  :disabled="humanVerificationSaving || humanForm.provider === 'None' || (!humanVerificationReady && !humanForm.enabled)"
                />
                <FieldContent>
                  <FieldLabel for="human-verification-enabled">{{ $t('ui.enableHumanVerification') }}</FieldLabel>
                  <FieldDescription>{{ $t('ui.humanVerificationProtectedOperations') }}</FieldDescription>
                </FieldContent>
              </Field>
              <Field orientation="horizontal">
                <Switch
                  id="human-verification-runtime-enabled"
                  v-model="humanForm.runtimeEnabled"
                  :disabled="humanVerificationSaving || !humanForm.enabled || humanForm.provider === 'None'"
                />
                <FieldContent>
                  <FieldLabel for="human-verification-runtime-enabled">{{ $t('ui.requireHumanVerificationForContainerOperations') }}</FieldLabel>
                  <FieldDescription>{{ $t('ui.containerHumanVerificationDescription') }}</FieldDescription>
                </FieldContent>
              </Field>

              <template v-if="humanForm.provider === 'Cap'">
                <Field>
                  <FieldLabel for="human-verification-cap-url">{{ $t('ui.capServerUrl') }}</FieldLabel>
                  <Input id="human-verification-cap-url" v-model="humanForm.capServerUrl" :disabled="humanVerificationSaving" :placeholder="$t('ui.capServerUrlPlaceholder')" maxlength="2048" />
                  <FieldDescription>{{ $t('ui.capServerUrlDescription') }}</FieldDescription>
                </Field>
                <Field>
                  <FieldLabel for="human-verification-cap-site-key">{{ $t('ui.siteKey') }}</FieldLabel>
                  <Input id="human-verification-cap-site-key" v-model="humanForm.capSiteKey" :disabled="humanVerificationSaving" maxlength="256" autocomplete="off" />
                </Field>
              </template>

              <template v-else-if="humanForm.provider === 'Turnstile'">
                <Field>
                  <FieldLabel for="human-verification-turnstile-site-key">{{ $t('ui.siteKey') }}</FieldLabel>
                  <Input id="human-verification-turnstile-site-key" v-model="humanForm.turnstileSiteKey" :disabled="humanVerificationSaving" maxlength="256" autocomplete="off" />
                </Field>
                <Field>
                  <FieldLabel for="human-verification-hostnames">{{ $t('ui.allowedHostnames') }}</FieldLabel>
                  <Textarea id="human-verification-hostnames" v-model="humanForm.turnstileAllowedHostnames" :disabled="humanVerificationSaving" rows="3" :placeholder="$t('ui.allowedHostnamePlaceholder')" />
                  <FieldDescription>{{ $t('ui.allowedHostnamesDescription') }}</FieldDescription>
                </Field>
              </template>

              <Field v-if="humanForm.provider !== 'None'">
                <FieldLabel>{{ $t('ui.providerSecret') }}</FieldLabel>
                <div class="flex flex-wrap items-center gap-2">
                  <Button type="button" variant="outline" :disabled="humanVerificationSaving" @click="setHumanSecretOpen(true)">
                    <KeyRound data-icon="inline-start" />
                    {{ selectedSecretConfigured ? $t('ui.replaceProviderSecret') : $t('ui.configureProviderSecret') }}
                  </Button>
                  <Badge :variant="selectedSecretConfigured ? 'secondary' : 'destructive'">
                    {{ selectedSecretConfigured ? $t('ui.configured') : $t('ui.notConfigured') }}
                  </Badge>
                </div>
                <FieldDescription>{{ $t('ui.providerSecretDescription') }}</FieldDescription>
              </Field>
            </FieldGroup>
            <div class="flex flex-wrap items-center gap-3">
              <Button
                type="submit"
                :disabled="humanVerificationSaving || !humanVerificationDirty || (humanForm.enabled && !humanVerificationReady)"
              >
                <Spinner v-if="humanVerificationSaving" data-icon="inline-start" />
                {{ $t('ui.saveChanges') }}
              </Button>
              <span class="text-sm text-muted-foreground">{{ $t('ui.currentProvider') }}: {{ humanVerificationProviderLabel }}</span>
            </div>
          </UiForm>
        </CardContent>
      </Card>

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
          <UiForm @submit.prevent="save">
            <FieldGroup>
              <Field>
                <FieldLabel for="public-base-url">{{ $t('ui.publiclyAccessibleAddress') }}</FieldLabel>
                <Input id="public-base-url" v-model="form.publicBaseUrl" required :placeholder="$t('ui.httpsCtfExampleCom')" />
                <FieldDescription>{{ $t('ui.usedToGenerateVerificationResetLinksInEmails') }}</FieldDescription>
              </Field>
              <div class="grid gap-4 sm:grid-cols-3">
                <Field>
                  <FieldLabel for="token-lifetime">{{ $t('ui.verificationTokenValidityPeriodMinutes') }}</FieldLabel>
                  <NumberInput id="token-lifetime" v-model.number="form.tokenLifetimeMinutes"  min="1" required />
                </Field>
                <Field>
                  <FieldLabel for="resend-cooldown">{{ $t('ui.resendCooldownSeconds') }}</FieldLabel>
                  <NumberInput id="resend-cooldown" v-model.number="form.resendCooldownSeconds"  min="0" required />
                </Field>
                <Field>
                  <FieldLabel for="smtp-timeout">{{ $t('ui.smtpTimeoutSeconds') }}</FieldLabel>
                  <NumberInput id="smtp-timeout" v-model.number="form.smtpTimeoutSeconds"  min="1" required />
                </Field>
              </div>
              <div class="grid gap-4 sm:grid-cols-3">
                <Field>
                  <FieldLabel for="reset-lifetime">{{ $t('ui.resetTokenValidityPeriodMinutes') }}</FieldLabel>
                  <NumberInput id="reset-lifetime" v-model.number="form.passwordResetTokenLifetimeMinutes"  min="1" required />
                </Field>
                <Field>
                  <FieldLabel for="reset-cooldown">{{ $t('ui.resetCooldownSeconds') }}</FieldLabel>
                  <NumberInput id="reset-cooldown" v-model.number="form.passwordResetCooldownSeconds"  min="0" required />
                </Field>
                <Field>
                  <FieldLabel for="reset-max">{{ $t('ui.resetFrequencyUpperLimitTimesHour') }}</FieldLabel>
                  <NumberInput id="reset-max" v-model.number="form.passwordResetMaxRequestsPerHour"  min="1" required />
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
                  <NumberInput id="smtp-port" v-model.number="form.smtpPort"  min="1" max="65535" required />
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
                <Button type="button" variant="outline" :disabled="sendingTest || !configuration.enabled || emailDirty" @click="sendTest">
                  <Spinner v-if="sendingTest" data-icon="inline-start" />
                  <Send v-else data-icon="inline-start" /> {{ $t('ui.sendTestEmail') }} </Button>
              </Field>
              <p class="text-sm text-muted-foreground"> {{ $t('ui.theTestEmailWillBeSentToTheEmailAddress') }} </p>
            </FieldGroup>
          </UiForm>
        </CardContent>
      </Card>
    </template>

    <Dialog :open="humanSecretOpen" @update:open="setHumanSecretOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ $t('ui.configureProviderSecret') }}</DialogTitle>
          <DialogDescription>{{ $t('ui.providerSecretDialogDescription', { provider: humanVerificationProviderLabel }) }}</DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel for="human-verification-secret">{{ $t('ui.providerSecret') }}</FieldLabel>
            <PasswordInput id="human-verification-secret" v-model="humanSecret" required maxlength="4096" autocomplete="new-password" />
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" :disabled="humanSecretSaving" @click="setHumanSecretOpen(false)">{{ $t('ui.cancel') }}</Button>
          <Button :disabled="humanSecretSaving || !humanSecret" @click="replaceHumanVerificationSecret">
            <Spinner v-if="humanSecretSaving" data-icon="inline-start" />
            {{ $t('ui.saveSecret') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

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
  </section>
</template>
