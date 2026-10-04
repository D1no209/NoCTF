<script setup lang="ts">

import { toRefs } from 'vue'
import type { AdminPlatformEmailPageViewState } from '~/features/routes/admin/platform/useAdminPlatformEmailPage'

const viewProps = defineProps<{ state: AdminPlatformEmailPageViewState }>()
const { KeyRound, RefreshCw, Send, ShieldCheck, configuration, humanVerification, humanForm, humanVerificationSaving, humanVerificationDirty, humanVerificationReady, humanVerificationProviderLabel, selectedSecretConfigured, humanSecretOpen, humanSecret, humanSecretSaving, capWorkload, capWorkloadForm, capWorkloadLoading, capWorkloadSaving, capWorkloadError, capWorkloadDirty, capWorkloadValid, capExpectedHashAttemptsLabel, capWorkloadRiskLabel, capWorkloadRiskVariant, loading, loadError, form, emailDirty, saving, passwordOpen, newPassword, passwordSaving, sendingTest, load, saveHumanVerification, replaceHumanVerificationSecret, loadCapWorkload, saveCapWorkload, save, replacePassword, sendTest, AdminDateTime, onClickPasswordOpen, onClickPasswordOpen2, setHumanSecretOpen } = toRefs(viewProps.state)
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
        <CardTitle>{{ $t('administration.label.emailHumanVerification') }}</CardTitle>
        <CardDescription>{{ $t('administration.platformEmail.description.platformConfigurationCouldLoaded') }}</CardDescription>
      </CardHeader>
      <CardContent>
        <Button variant="outline" @click="load">
          <RefreshCw data-icon="inline-start" />
          {{ $t('common.label.retry') }}
        </Button>
      </CardContent>
    </Card>

    <template v-else-if="configuration">
      <Card v-if="humanVerification">
        <CardHeader class="flex flex-row items-start justify-between gap-4">
          <div class="flex min-w-0 flex-col gap-1">
            <CardTitle class="flex items-center gap-2">
              <ShieldCheck class="size-5" />
              {{ $t('administration.label.humanVerificationConfiguration') }}
            </CardTitle>
            <CardDescription>{{ $t('administration.label.humanVerificationAdminDescription') }}</CardDescription>
          </div>
          <Badge :variant="humanVerificationReady ? 'secondary' : 'outline'">
            {{ humanVerificationReady ? $t('administration.label.configurationComplete') : $t('administration.label.configurationIncomplete') }}
          </Badge>
        </CardHeader>
        <CardContent>
          <UiForm class="flex flex-col gap-5" @submit.prevent="saveHumanVerification">
            <FieldGroup>
              <Field>
                <FieldLabel for="human-verification-provider">{{ $t('common.label.provider') }}</FieldLabel>
                <Select v-model="humanForm.provider" :disabled="humanVerificationSaving">
                  <SelectTrigger id="human-verification-provider" class="w-full">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectGroup>
                      <SelectItem value="None">{{ $t('administration.label.disabled') }}</SelectItem>
                      <SelectItem value="Cap">{{ $t('administration.label.capProvider') }}</SelectItem>
                      <SelectItem value="Turnstile">{{ $t('administration.label.turnstileProvider') }}</SelectItem>
                    </SelectGroup>
                  </SelectContent>
                </Select>
                <FieldDescription>{{ $t('administration.label.humanVerificationProviderDescription') }}</FieldDescription>
              </Field>
              <Field orientation="horizontal">
                <Switch
                  id="human-verification-enabled"
                  v-model="humanForm.enabled"
                  :disabled="humanVerificationSaving || humanForm.provider === 'None' || (!humanVerificationReady && !humanForm.enabled)"
                />
                <FieldContent>
                  <FieldLabel for="human-verification-enabled">{{ $t('administration.label.enableHumanVerification') }}</FieldLabel>
                  <FieldDescription>{{ $t('administration.label.humanVerificationProtectedOperations') }}</FieldDescription>
                </FieldContent>
              </Field>
              <Field orientation="horizontal">
                <Switch
                  id="human-verification-runtime-enabled"
                  v-model="humanForm.runtimeEnabled"
                  :disabled="humanVerificationSaving || !humanForm.enabled || humanForm.provider === 'None'"
                />
                <FieldContent>
                  <FieldLabel for="human-verification-runtime-enabled">{{ $t('administration.platformEmail.description.requireHumanVerificationContainer') }}</FieldLabel>
                  <FieldDescription>{{ $t('administration.label.containerHumanVerificationDescription') }}</FieldDescription>
                </FieldContent>
              </Field>
              <Field orientation="horizontal">
                <Switch
                  id="human-verification-evaluation-enabled"
                  v-model="humanForm.evaluationEnabled"
                  :disabled="humanVerificationSaving || !humanForm.enabled || humanForm.provider === 'None'"
                />
                <FieldContent>
                  <FieldLabel for="human-verification-evaluation-enabled">{{ $t('administration.platformEmail.description.requireHumanVerificationFlag') }}</FieldLabel>
                  <FieldDescription>{{ $t('administration.platformEmail.label.flagSubmissionHumanVerification') }}</FieldDescription>
                </FieldContent>
              </Field>

              <template v-if="humanForm.provider === 'Cap'">
                <Field>
                  <FieldLabel for="human-verification-cap-url">{{ $t('administration.label.capServerUrl') }}</FieldLabel>
                  <Input id="human-verification-cap-url" v-model="humanForm.capServerUrl" :disabled="humanVerificationSaving" :placeholder="$t('administration.label.capServerUrlPlaceholder')" maxlength="2048" />
                  <FieldDescription>{{ $t('administration.label.capServerUrlDescription') }}</FieldDescription>
                </Field>
                <Field>
                  <FieldLabel for="human-verification-cap-site-key">{{ $t('administration.label.siteKey') }}</FieldLabel>
                  <Input id="human-verification-cap-site-key" v-model="humanForm.capSiteKey" :disabled="humanVerificationSaving" maxlength="256" autocomplete="off" />
                </Field>

                <FieldSeparator>{{ $t('administration.platformEmail.label.capProofWorkConfiguration') }}</FieldSeparator>

                <div class="flex flex-col gap-1">
                  <h3 class="text-base font-semibold text-primary">{{ $t('administration.label.capComputationalWorkload') }}</h3>
                  <p class="text-sm text-muted-foreground">{{ $t('administration.label.capComputationalWorkloadDescription') }}</p>
                </div>

                <Skeleton v-if="capWorkloadLoading" class="h-28 w-full" />

                <Alert v-else-if="capWorkloadError" variant="destructive">
                  <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
                    <span>{{ $message(capWorkloadError) }}</span>
                    <Button type="button" variant="outline" size="sm" @click="loadCapWorkload">
                      <RefreshCw data-icon="inline-start" />
                      {{ $t('common.label.retry') }}
                    </Button>
                  </AlertDescription>
                </Alert>

                <template v-else-if="capWorkload">
                  <div class="grid gap-4 sm:grid-cols-3">
                    <Field>
                      <FieldLabel for="human-verification-cap-difficulty">{{ $t('administration.label.capDifficulty') }}</FieldLabel>
                      <NumberInput
                        id="human-verification-cap-difficulty"
                        v-model.number="capWorkloadForm.difficulty"
                        :disabled="capWorkloadSaving"
                        min="1"
                        max="8"
                        required
                      />
                      <FieldDescription>{{ $t('administration.label.capDifficultyDescription') }}</FieldDescription>
                    </Field>
                    <Field>
                      <FieldLabel for="human-verification-cap-challenge-count">{{ $t('administration.label.capChallengeCount') }}</FieldLabel>
                      <NumberInput
                        id="human-verification-cap-challenge-count"
                        v-model.number="capWorkloadForm.challengeCount"
                        :disabled="capWorkloadSaving"
                        min="1"
                        max="500"
                        required
                      />
                      <FieldDescription>{{ $t('administration.label.capChallengeCountDescription') }}</FieldDescription>
                    </Field>
                    <Field>
                      <FieldLabel>{{ $t('administration.label.capChallengeSize') }}</FieldLabel>
                      <div class="flex h-10 items-center">
                        <Badge variant="outline">{{ capWorkload.challengeSize ?? 32 }}</Badge>
                      </div>
                      <FieldDescription>{{ $t('administration.label.capChallengeSizeDescription') }}</FieldDescription>
                    </Field>
                  </div>

                  <div class="flex flex-wrap items-center gap-3 rounded-xl bg-muted/45 px-4 py-3">
                    <span class="text-sm text-muted-foreground">{{ $t('administration.label.capExpectedHashAttempts') }}</span>
                    <strong class="text-sm text-foreground">{{ capExpectedHashAttemptsLabel }}</strong>
                    <Badge :variant="capWorkloadRiskVariant">{{ capWorkloadRiskLabel }}</Badge>
                  </div>
                  <FieldDescription v-if="humanVerificationDirty">
                    {{ $t('administration.platformEmail.label.saveCapProviderWorkload') }}
                  </FieldDescription>
                  <div>
                    <Button
                      type="button"
                      :disabled="capWorkloadSaving || !capWorkloadDirty || !capWorkloadValid || humanVerificationDirty"
                      @click="saveCapWorkload"
                    >
                      <Spinner v-if="capWorkloadSaving" data-icon="inline-start" />
                      {{ $t('administration.label.saveCapWorkload') }}
                    </Button>
                  </div>
                </template>
              </template>

              <template v-else-if="humanForm.provider === 'Turnstile'">
                <Field>
                  <FieldLabel for="human-verification-turnstile-site-key">{{ $t('administration.label.siteKey') }}</FieldLabel>
                  <Input id="human-verification-turnstile-site-key" v-model="humanForm.turnstileSiteKey" :disabled="humanVerificationSaving" maxlength="256" autocomplete="off" />
                </Field>
                <Field>
                  <FieldLabel for="human-verification-hostnames">{{ $t('administration.label.allowedHostnames') }}</FieldLabel>
                  <Textarea id="human-verification-hostnames" v-model="humanForm.turnstileAllowedHostnames" :disabled="humanVerificationSaving" rows="3" :placeholder="$t('administration.label.allowedHostnamePlaceholder')" />
                  <FieldDescription>{{ $t('administration.label.allowedHostnamesDescription') }}</FieldDescription>
                </Field>
              </template>

              <Field v-if="humanForm.provider !== 'None'">
                <FieldLabel>{{ $t('administration.label.providerSecret') }}</FieldLabel>
                <div class="flex flex-wrap items-center gap-2">
                  <Button type="button" variant="outline" :disabled="humanVerificationSaving" @click="setHumanSecretOpen(true)">
                    <KeyRound data-icon="inline-start" />
                    {{ selectedSecretConfigured ? $t('administration.label.replaceProviderSecret') : $t('administration.label.configureProviderSecret') }}
                  </Button>
                  <Badge :variant="selectedSecretConfigured ? 'secondary' : 'destructive'">
                    {{ selectedSecretConfigured ? $t('administration.label.configured.emailPageView') : $t('administration.label.configured') }}
                  </Badge>
                </div>
                <FieldDescription>{{ $t('administration.label.providerSecretDescription') }}</FieldDescription>
              </Field>
            </FieldGroup>
            <div class="flex flex-wrap items-center gap-3">
              <Button
                type="submit"
                :disabled="humanVerificationSaving || !humanVerificationDirty || (humanForm.enabled && !humanVerificationReady)"
              >
                <Spinner v-if="humanVerificationSaving" data-icon="inline-start" />
                {{ $t('administration.label.saveChanges') }}
              </Button>
              <span class="text-sm text-muted-foreground">{{ $t('administration.label.provider') }}: {{ humanVerificationProviderLabel }}</span>
            </div>
          </UiForm>
        </CardContent>
      </Card>

      <Card>
        <CardHeader class="flex flex-row items-center justify-between gap-4">
          <div>
            <CardTitle>{{ $t('administration.label.emailVerificationConfiguration') }}</CardTitle>
            <CardDescription>
              {{ $t('administration.platformEmail.description.registrationVerificationPasswordReset') }}
              <component :is="AdminDateTime" :value="dateIso(configuration.updatedAt)" />
            </CardDescription>
          </div>
          <div class="flex items-center gap-2">
            <Switch id="email-enabled" v-model="form.enabled" />
            <Label for="email-enabled">{{ $t('administration.label.enableEmailSending') }}</Label>
          </div>
        </CardHeader>
        <CardContent>
          <UiForm @submit.prevent="save">
            <FieldGroup>
              <Field>
                <FieldLabel for="public-base-url">{{ $t('administration.label.publiclyAccessibleAddress') }}</FieldLabel>
                <Input id="public-base-url" v-model="form.publicBaseUrl" required :placeholder="$t('administration.label.httpsCtfExampleCom')" />
                <FieldDescription>{{ $t('administration.platformEmail.description.generateVerificationResetLinks') }}</FieldDescription>
              </Field>
              <div class="grid gap-4 sm:grid-cols-3">
                <Field>
                  <FieldLabel for="token-lifetime">{{ $t('administration.platformEmail.label.verificationTokenValidityPeriod') }}</FieldLabel>
                  <NumberInput id="token-lifetime" v-model.number="form.tokenLifetimeMinutes"  min="1" required />
                </Field>
                <Field>
                  <FieldLabel for="resend-cooldown">{{ $t('administration.label.resendCooldownSeconds') }}</FieldLabel>
                  <NumberInput id="resend-cooldown" v-model.number="form.resendCooldownSeconds"  min="0" required />
                </Field>
                <Field>
                  <FieldLabel for="smtp-timeout">{{ $t('administration.label.smtpTimeoutSeconds') }}</FieldLabel>
                  <NumberInput id="smtp-timeout" v-model.number="form.smtpTimeoutSeconds"  min="1" required />
                </Field>
              </div>
              <div class="grid gap-4 sm:grid-cols-3">
                <Field>
                  <FieldLabel for="reset-lifetime">{{ $t('administration.platformEmail.label.resetTokenValidityPeriod') }}</FieldLabel>
                  <NumberInput id="reset-lifetime" v-model.number="form.passwordResetTokenLifetimeMinutes"  min="1" required />
                </Field>
                <Field>
                  <FieldLabel for="reset-cooldown">{{ $t('administration.label.resetCooldownSeconds') }}</FieldLabel>
                  <NumberInput id="reset-cooldown" v-model.number="form.passwordResetCooldownSeconds"  min="0" required />
                </Field>
                <Field>
                  <FieldLabel for="reset-max">{{ $t('administration.platformEmail.description.resetFrequencyUpperLimit') }}</FieldLabel>
                  <NumberInput id="reset-max" v-model.number="form.passwordResetMaxRequestsPerHour"  min="1" required />
                </Field>
              </div>

              <FieldSeparator>{{ $t('administration.label.smtpServer') }}</FieldSeparator>

              <div class="grid gap-4 sm:grid-cols-3">
                <Field class="sm:col-span-2">
                  <FieldLabel for="smtp-host">{{ $t('administration.label.host') }}</FieldLabel>
                  <Input id="smtp-host" v-model="form.smtpHost" required :placeholder="$t('administration.label.smtpExampleCom')" />
                </Field>
                <Field>
                  <FieldLabel for="smtp-port">{{ $t('administration.label.port') }}</FieldLabel>
                  <NumberInput id="smtp-port" v-model.number="form.smtpPort"  min="1" max="65535" required />
                </Field>
              </div>
              <div class="grid gap-4 sm:grid-cols-3">
                <Field>
                  <FieldLabel for="smtp-security">{{ $t('administration.label.encryptionMethod') }}</FieldLabel>
                  <Select v-model="form.smtpSecurityMode">
                    <SelectTrigger id="smtp-security" class="w-full">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectGroup>
                        <SelectItem value="None">{{ $t('administration.label.encryption') }}</SelectItem>
                        <SelectItem value="SslOnConnect">{{ $t('administration.label.sslTls') }}</SelectItem>
                        <SelectItem value="StartTls">{{ $t('administration.label.starttls') }}</SelectItem>
                      </SelectGroup>
                    </SelectContent>
                  </Select>
                </Field>
                <Field class="sm:col-span-2">
                  <FieldLabel for="smtp-username">{{ $t('common.label.username') }}</FieldLabel>
                  <Input id="smtp-username" v-model="form.smtpUserName" required autocomplete="off" />
                </Field>
              </div>
              <div class="grid gap-4 sm:grid-cols-2">
                <Field>
                  <FieldLabel for="smtp-from-address">{{ $t('administration.label.shippingAddress') }}</FieldLabel>
                  <Input id="smtp-from-address" v-model="form.smtpFromAddress" type="email" required :placeholder="$t('administration.label.noreplyExampleCom')" />
                </Field>
                <Field>
                  <FieldLabel for="smtp-from-name">{{ $t('administration.label.senderName') }}</FieldLabel>
                  <Input id="smtp-from-name" v-model="form.smtpFromName" required :placeholder="$t('common.label.noctf')" />
                </Field>
              </div>
              <Field orientation="horizontal" class="flex-wrap gap-4">
                <Button type="submit" :disabled="saving">
                  <Spinner v-if="saving" data-icon="inline-start" /> {{ $t('administration.label.saveConfiguration') }} </Button>
                <Button type="button" variant="outline" @click="onClickPasswordOpen(true)">
                  <KeyRound data-icon="inline-start" /> {{ $t('administration.label.changeSmtpPassword') }} <Badge :variant="configuration.smtpPasswordConfigured ? 'secondary' : 'destructive'" class="ml-2">
                    {{ configuration.smtpPasswordConfigured ? $t('administration.label.configured.emailPageView') : $t('administration.label.configured') }}
                  </Badge>
                </Button>
                <Button type="button" variant="outline" :disabled="sendingTest || !configuration.enabled || emailDirty" @click="sendTest">
                  <Spinner v-if="sendingTest" data-icon="inline-start" />
                  <Send v-else data-icon="inline-start" /> {{ $t('administration.label.sendTestEmail') }} </Button>
              </Field>
              <p class="text-sm text-muted-foreground"> {{ $t('common.description.testEmailSentEmail') }} </p>
            </FieldGroup>
          </UiForm>
        </CardContent>
      </Card>
    </template>

    <Dialog :open="humanSecretOpen" @update:open="setHumanSecretOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ $t('administration.label.configureProviderSecret') }}</DialogTitle>
          <DialogDescription>{{ $t('administration.label.providerSecretDialogDescription', { provider: humanVerificationProviderLabel }) }}</DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel for="human-verification-secret">{{ $t('administration.label.providerSecret') }}</FieldLabel>
            <PasswordInput id="human-verification-secret" v-model="humanSecret" required maxlength="4096" autocomplete="new-password" />
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" :disabled="humanSecretSaving" @click="setHumanSecretOpen(false)">{{ $t('common.action.cancel') }}</Button>
          <Button :disabled="humanSecretSaving || !humanSecret" @click="replaceHumanVerificationSecret">
            <Spinner v-if="humanSecretSaving" data-icon="inline-start" />
            {{ $t('administration.label.saveSecret') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="passwordOpen">
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{{ $t('administration.label.changeSmtpPassword') }}</DialogTitle>
          <DialogDescription>{{ $t('administration.platformEmail.description.passwordSmtpAuthentication') }}</DialogDescription>
        </DialogHeader>
        <FieldGroup>
          <Field>
            <FieldLabel for="smtp-password">{{ $t('common.label.newPassword') }}</FieldLabel>
            <PasswordInput id="smtp-password" v-model="newPassword" required autocomplete="new-password" />
          </Field>
        </FieldGroup>
        <DialogFooter>
          <Button variant="outline" @click="onClickPasswordOpen2(false)">{{ $t('common.action.cancel') }}</Button>
          <Button :disabled="passwordSaving || !newPassword" @click="replacePassword">
            <Spinner v-if="passwordSaving" data-icon="inline-start" /> {{ $t('administration.label.savePassword') }} </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </section>
</template>
