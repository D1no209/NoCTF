<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminPlatformAuthenticationPageViewState } from '~/features/routes/admin/platform/useAdminPlatformAuthenticationPage'

const viewProps = defineProps<{ state: AdminPlatformAuthenticationPageViewState }>()
const { Plus, RotateCw, FlaskConical, configuration, loading, loadError, globalForm, globalSaving, providerOpen, providerSaving, providerError, providerForm, secretOpen, secretProvider, secret, secretSaving, testingId, load, saveGlobal, openCreateProvider, openEditProvider, saveProvider, openSecret, replaceSecret, testConnection, testAuthentication } = toRefs(viewProps.state)
</script>

<template>
  <section class="flex min-w-0 flex-col gap-6">
    <Alert v-if="loadError" variant="destructive"><AlertDescription>{{ $message(loadError) }}</AlertDescription></Alert>
    <template v-if="loading">
      <Skeleton class="h-48 w-full" />
      <Skeleton class="h-64 w-full" />
    </template>
    <template v-else-if="configuration">
      <Card>
        <CardHeader>
          <CardTitle>{{ $t('sso.authenticationSettings') }}</CardTitle>
          <CardDescription>{{ $t('sso.authenticationSettingsDescription') }}</CardDescription>
        </CardHeader>
        <CardContent>
          <UiForm class="flex flex-col gap-5" @submit.prevent="saveGlobal">
            <FieldGroup>
              <Field orientation="horizontal">
                <Switch id="sso-enabled" v-model="globalForm.enabled" :disabled="globalSaving" />
                <FieldContent>
                  <FieldLabel for="sso-enabled">{{ $t('sso.enableSingleSignOn') }}</FieldLabel>
                  <FieldDescription>{{ $t('sso.enableSingleSignOnDescription') }}</FieldDescription>
                </FieldContent>
              </Field>
              <Field>
                <FieldLabel for="sso-public-base-url">{{ $t('sso.publicBaseUrl') }}</FieldLabel>
                <Input id="sso-public-base-url" v-model="globalForm.publicBaseUrl" type="url" required maxlength="2048" :placeholder="$t('sso.publicBaseUrlPlaceholder')" />
                <FieldDescription>{{ $t('sso.publicBaseUrlDescription') }}</FieldDescription>
              </Field>
            </FieldGroup>
            <Button type="submit" class="self-start" :disabled="globalSaving">
              <Spinner v-if="globalSaving" data-icon="inline-start" />{{ $t('administration.label.saveConfiguration') }}
            </Button>
          </UiForm>
        </CardContent>
      </Card>

      <Card>
        <CardHeader class="flex flex-row items-center justify-between gap-4">
          <div>
            <CardTitle>{{ $t('sso.identityProviders') }}</CardTitle>
            <CardDescription>{{ $t('sso.identityProvidersDescription') }}</CardDescription>
          </div>
          <Button @click="openCreateProvider"><Plus data-icon="inline-start" />{{ $t('sso.addProvider') }}</Button>
        </CardHeader>
        <CardContent class="flex flex-col gap-3">
          <Empty v-if="!configuration.providers?.length">
            <EmptyHeader><EmptyTitle>{{ $t('sso.noProviders') }}</EmptyTitle></EmptyHeader>
          </Empty>
          <div v-for="provider in configuration.providers" v-else :key="provider.id ?? undefined" class="flex flex-col gap-4 rounded-xl border p-4 sm:flex-row sm:items-center">
            <img
              v-if="provider.iconUrl"
              :src="provider.iconUrl"
              class="size-10 shrink-0 rounded-lg object-contain"
              alt=""
              aria-hidden="true"
              loading="lazy"
              decoding="async"
              referrerpolicy="no-referrer"
            >
            <div class="min-w-0 flex-1">
              <div class="flex flex-wrap items-center gap-2">
                <p class="font-semibold">{{ provider.name }}</p>
                <Badge variant="outline">{{ provider.protocol }}</Badge>
                <Badge :variant="provider.enabled ? 'secondary' : 'outline'">{{ provider.enabled ? $t('sso.enabled') : $t('administration.label.disabled') }}</Badge>
              </div>
              <p class="mt-1 truncate text-xs text-muted-foreground">{{ provider.oidc?.issuer || provider.cas?.identityNamespace }}</p>
              <p class="mt-1 break-all font-mono text-xs text-muted-foreground">{{ $t('sso.providerId') }}: {{ provider.id }}</p>
            </div>
            <div class="flex flex-wrap gap-2">
              <Button variant="outline" size="sm" :disabled="Boolean(testingId)" @click="testConnection(provider)">
                <Spinner v-if="testingId === provider.id" data-icon="inline-start" /><FlaskConical v-else data-icon="inline-start" />{{ $t('sso.testConnection') }}
              </Button>
              <Button variant="outline" size="sm" :disabled="Boolean(testingId)" @click="testAuthentication(provider)">{{ $t('sso.testAuthentication') }}</Button>
              <Button v-if="provider.protocol === 'Oidc'" variant="outline" size="sm" @click="openSecret(provider)"><RotateCw data-icon="inline-start" />{{ $t('sso.replaceSecret') }}</Button>
              <Button variant="ghost" size="sm" @click="openEditProvider(provider)">{{ $t('administration.label.edit') }}</Button>
            </div>
          </div>
        </CardContent>
      </Card>
    </template>
    <Button v-else variant="outline" class="self-start" @click="load">{{ $t('common.label.retry') }}</Button>

    <Dialog v-model:open="providerOpen">
      <DialogContent class="sm:max-w-2xl">
        <DialogHeader>
          <DialogTitle>{{ providerForm.id ? $t('sso.editProvider') : $t('sso.addProvider') }}</DialogTitle>
          <DialogDescription>{{ $t('sso.providerFormDescription') }}</DialogDescription>
        </DialogHeader>
        <ScrollSurface axis="y" class="max-h-[68vh] pr-3">
        <UiForm class="flex flex-col gap-5" @submit.prevent="saveProvider">
          <FieldGroup>
            <Field><FieldLabel for="sso-provider-name">{{ $t('administration.label.name') }}</FieldLabel><Input id="sso-provider-name" v-model="providerForm.name" required maxlength="100" /></Field>
            <Field>
              <FieldLabel for="sso-provider-icon-url">{{ $t('sso.iconUrl') }}</FieldLabel>
              <Input id="sso-provider-icon-url" v-model="providerForm.iconUrl" type="url" maxlength="2048" :placeholder="$t('sso.iconUrlPlaceholder')" />
              <FieldDescription>{{ $t('sso.iconUrlDescription') }}</FieldDescription>
            </Field>
            <Field>
              <FieldLabel for="sso-provider-protocol">{{ $t('sso.protocol') }}</FieldLabel>
              <Select v-model="providerForm.protocol" :disabled="providerSaving || Boolean(providerForm.id)">
                <SelectTrigger id="sso-provider-protocol"><SelectValue /></SelectTrigger>
                <SelectContent><SelectItem value="Oidc">{{ $t('sso.oidc') }}</SelectItem><SelectItem value="Cas">{{ $t('account.label.cas') }}</SelectItem></SelectContent>
              </Select>
            </Field>
            <div class="grid gap-4 sm:grid-cols-3">
              <Field orientation="horizontal"><Switch id="sso-provider-enabled" v-model="providerForm.enabled" /><FieldLabel for="sso-provider-enabled">{{ $t('sso.enabled') }}</FieldLabel></Field>
              <Field orientation="horizontal"><Switch id="sso-provider-login" v-model="providerForm.allowLogin" /><FieldLabel for="sso-provider-login">{{ $t('sso.allowLogin') }}</FieldLabel></Field>
              <Field orientation="horizontal"><Switch id="sso-provider-binding" v-model="providerForm.allowBinding" /><FieldLabel for="sso-provider-binding">{{ $t('sso.allowBinding') }}</FieldLabel></Field>
            </div>
            <Field><FieldLabel for="sso-provider-hosts">{{ $t('sso.allowedHosts') }}</FieldLabel><Textarea id="sso-provider-hosts" v-model="providerForm.allowedHosts" rows="3" required /><FieldDescription>{{ $t('sso.oneValuePerLine') }}</FieldDescription></Field>
            <Field><FieldLabel for="sso-provider-timeout">{{ $t('sso.timeoutSeconds') }}</FieldLabel><NumberInput id="sso-provider-timeout" v-model.number="providerForm.timeoutSeconds" min="1" max="30" required /></Field>

            <template v-if="providerForm.protocol === 'Oidc'">
              <Field><FieldLabel for="sso-oidc-issuer">{{ $t('sso.issuer') }}</FieldLabel><Input id="sso-oidc-issuer" v-model="providerForm.issuer" type="url" required maxlength="2048" /></Field>
              <Field><FieldLabel for="sso-oidc-discovery">{{ $t('sso.discoveryUrl') }}</FieldLabel><Input id="sso-oidc-discovery" v-model="providerForm.discoveryUrl" type="url" required maxlength="2048" /></Field>
              <Field><FieldLabel for="sso-oidc-client-id">{{ $t('sso.clientId') }}</FieldLabel><Input id="sso-oidc-client-id" v-model="providerForm.clientId" required maxlength="512" /></Field>
              <Field><FieldLabel for="sso-oidc-scopes">{{ $t('sso.scopes') }}</FieldLabel><Textarea id="sso-oidc-scopes" v-model="providerForm.scopes" rows="3" required /><FieldDescription>{{ $t('sso.oneValuePerLine') }}</FieldDescription></Field>
              <Field><FieldLabel for="sso-oidc-display-name">{{ $t('sso.displayNameClaim') }}</FieldLabel><Input id="sso-oidc-display-name" v-model="providerForm.displayNameClaim" required maxlength="128" /></Field>
              <Field orientation="horizontal"><Switch id="sso-oidc-userinfo" v-model="providerForm.readUserInfo" /><FieldContent><FieldLabel for="sso-oidc-userinfo">{{ $t('sso.readUserInfo') }}</FieldLabel></FieldContent></Field>
            </template>
            <template v-else>
              <Field><FieldLabel for="sso-cas-namespace">{{ $t('sso.identityNamespace') }}</FieldLabel><Input id="sso-cas-namespace" v-model="providerForm.identityNamespace" required maxlength="512" /></Field>
              <Field><FieldLabel for="sso-cas-login">{{ $t('sso.loginUrl') }}</FieldLabel><Input id="sso-cas-login" v-model="providerForm.loginUrl" type="url" required maxlength="2048" /></Field>
              <Field><FieldLabel for="sso-cas-validate">{{ $t('sso.serviceValidateUrl') }}</FieldLabel><Input id="sso-cas-validate" v-model="providerForm.serviceValidateUrl" type="url" required maxlength="2048" /></Field>
              <Field><FieldLabel for="sso-cas-display-name">{{ $t('sso.displayNameAttribute') }}</FieldLabel><Input id="sso-cas-display-name" v-model="providerForm.displayNameAttribute" required maxlength="128" /></Field>
            </template>
          </FieldGroup>
          <Alert v-if="providerError" variant="destructive"><AlertDescription>{{ $message(providerError) }}</AlertDescription></Alert>
          <DialogFooter><Button type="submit" :disabled="providerSaving"><Spinner v-if="providerSaving" data-icon="inline-start" />{{ $t('common.action.save') }}</Button></DialogFooter>
        </UiForm>
        </ScrollSurface>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="secretOpen">
      <DialogContent>
        <DialogHeader><DialogTitle>{{ $t('sso.replaceSecret') }}</DialogTitle><DialogDescription>{{ secretProvider?.name }}</DialogDescription></DialogHeader>
        <Field><FieldLabel for="sso-client-secret">{{ $t('sso.clientSecret') }}</FieldLabel><PasswordInput id="sso-client-secret" v-model="secret" autocomplete="new-password" /></Field>
        <DialogFooter><Button :disabled="secretSaving || !secret" @click="replaceSecret"><Spinner v-if="secretSaving" data-icon="inline-start" />{{ $t('sso.replaceSecret') }}</Button></DialogFooter>
      </DialogContent>
    </Dialog>
  </section>
</template>
