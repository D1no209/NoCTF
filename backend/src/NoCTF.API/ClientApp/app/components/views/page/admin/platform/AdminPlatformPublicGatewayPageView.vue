<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminPlatformPublicGatewayPageViewState } from '~/features/routes/admin/platform/useAdminPlatformPublicGatewayPage'

const viewProps = defineProps<{ state: AdminPlatformPublicGatewayPageViewState }>()
const { Globe, RefreshCw, publicGatewayFailure, publicGatewayState, configuration, status, loading, saving, error, statusError, fieldErrors, form, dirty, capability, application, load, save } = toRefs(viewProps.state)
</script>

<template>
  <section class="flex min-w-0 flex-col gap-6">
    <header class="flex flex-wrap items-start justify-between gap-3">
      <div class="flex flex-col gap-1">
        <h2 class="text-lg font-semibold">{{ $t('ui.intranetTunneling') }}</h2>
        <p class="max-w-2xl text-sm text-muted-foreground">{{ $t('ui.publicConnectivityIsIndependentOfRuntimesDisablingItDoesNot') }}</p>
      </div>
      <Button variant="outline" :disabled="loading || application.polling.value" @click="application.start()"><RefreshCw data-icon="inline-start" />{{ $t('ui.checkGatewayStatus') }}</Button>
    </header>
    <Skeleton v-if="loading" class="h-64 w-full" />
    <template v-else>
      <Alert v-if="error" variant="destructive"><AlertDescription class="break-words">{{ $message(error) }} <Button v-if="!configuration" variant="outline" size="sm" @click="load">{{ $t('ui.retry') }}</Button></AlertDescription></Alert>
      <Alert v-if="!capability"><Globe /><AlertDescription>{{ $t('ui.noPublicConnectorIsPairedContactOperationsToCompleteThe') }}</AlertDescription></Alert>
      <Alert v-else-if="!capability.namespaceIsolationAvailable" variant="destructive"><AlertDescription>{{ publicGatewayFailure('GatewaySafetyCheckFailed') }}<p v-if="capability.configurationError" class="break-words">{{ $message(capability.configurationError) }}</p></AlertDescription></Alert>
      <form v-if="configuration" class="flex max-w-3xl flex-col gap-6" @submit.prevent="save">
        <FieldGroup>
          <Field orientation="horizontal">
            <Switch id="gateway-enabled" v-model="form.enabled" :disabled="!capability?.namespaceIsolationAvailable || saving" />
            <FieldContent><FieldLabel for="gateway-enabled">{{ $t('ui.enableChallengeTunneling') }}</FieldLabel><FieldDescription>{{ $t('ui.onlyApprovedPlayerAndPracticeContainersArePublishedExcludingCheckers') }}</FieldDescription></FieldContent>
          </Field>
          <Field><FieldLabel>{{ $t('ui.pairedConnector') }}</FieldLabel><p class="break-all font-mono text-sm">{{ capability?.connectorId ?? $t('ui.symbol') }} · {{ capability?.runnerId ?? $t('ui.symbol') }}</p><FieldDescription>{{ $t('ui.connectorAndPortCapabilitiesAreDeploymentControlledArbitraryProxyTargets') }}</FieldDescription></Field>
          <Field :data-invalid="Boolean(fieldErrors.publicOrigin)">
            <FieldLabel for="gateway-origin">{{ $t('ui.publicPlatformOrigin') }}</FieldLabel>
            <Select v-model="form.publicOrigin" :disabled="!capability || saving"><SelectTrigger id="gateway-origin" :aria-invalid="Boolean(fieldErrors.publicOrigin)"><SelectValue /></SelectTrigger><SelectContent><SelectGroup><SelectItem v-for="origin in capability?.approvedOrigins ?? []" :key="origin" :value="origin">{{ origin }}</SelectItem></SelectGroup></SelectContent></Select>
            <FieldError v-if="fieldErrors.publicOrigin">{{ $message(fieldErrors.publicOrigin) }}</FieldError>
          </Field>
          <Field :data-invalid="Boolean(fieldErrors.directOrigins)"><FieldLabel for="gateway-direct">{{ $t('ui.directPlatformOrigins') }}</FieldLabel><Textarea id="gateway-direct" v-model="form.directOrigins" :disabled="saving" :aria-invalid="Boolean(fieldErrors.directOrigins)" rows="3" :placeholder="$t('ui.httpsNoctfExampleLocal')" /><FieldDescription>{{ $t('ui.oneFullOriginPerLineEachOriginHasASeparate') }}</FieldDescription><FieldError v-if="fieldErrors.directOrigins">{{ $message(fieldErrors.directOrigins) }}</FieldError></Field>
          <Field :data-invalid="Boolean(fieldErrors.publicRuntimeHost)"><FieldLabel for="gateway-host">{{ $t('ui.publicChallengeHost') }}</FieldLabel><Input id="gateway-host" v-model="form.publicRuntimeHost" :disabled="saving" :aria-invalid="Boolean(fieldErrors.publicRuntimeHost)" maxlength="253" /><FieldError v-if="fieldErrors.publicRuntimeHost">{{ $message(fieldErrors.publicRuntimeHost) }}</FieldError></Field>
          <Field :data-invalid="Boolean(fieldErrors.directRuntimeHostOverride)"><FieldLabel for="gateway-direct-host">{{ $t('ui.directDisplayHostOverride') }}</FieldLabel><Input id="gateway-direct-host" v-model="form.directRuntimeHostOverride" :disabled="saving" :aria-invalid="Boolean(fieldErrors.directRuntimeHostOverride)" maxlength="253" /><FieldDescription>{{ $t('ui.leaveBlankToKeepExistingDirectConnectionText') }}</FieldDescription><FieldError v-if="fieldErrors.directRuntimeHostOverride">{{ $message(fieldErrors.directRuntimeHostOverride) }}</FieldError></Field>
          <Field :data-invalid="Boolean(fieldErrors.maxPublishedPorts)"><FieldLabel for="gateway-quota">{{ $t('ui.maximumPublicPorts') }}</FieldLabel><Input id="gateway-quota" v-model.number="form.maxPublishedPorts" type="number" min="1" :max="capability?.maximumPorts ?? 1" :disabled="saving" :aria-invalid="Boolean(fieldErrors.maxPublishedPorts)" /><FieldDescription>{{ $t('ui.gatewayAllowedPortRange') }}: {{ capability?.firstPort ?? $t('ui.symbol') }}–{{ capability?.lastPort ?? $t('ui.symbol') }} · {{ $t('ui.gatewayReservedPorts') }}: {{ capability?.reservedPorts?.join(', ') || $t('ui.symbol') }}</FieldDescription><FieldError v-if="fieldErrors.maxPublishedPorts">{{ $message(fieldErrors.maxPublishedPorts) }}</FieldError></Field>
        </FieldGroup>
        <Alert v-if="dirty"><AlertDescription>{{ $t('ui.gatewayChangesMayInterruptPublicConnectionsDirectRuntimesAreNot') }}</AlertDescription></Alert>
        <div class="flex flex-wrap items-center gap-3"><Button type="submit" :disabled="saving || !capability || !dirty"><Spinner v-if="saving" data-icon="inline-start" />{{ $t('ui.saveChanges') }}</Button><span v-if="dirty" class="text-sm text-muted-foreground">{{ $t('ui.unsavedGatewayChanges') }}</span></div>
      </form>
      <Separator />
      <section class="flex min-w-0 flex-col gap-3" aria-live="polite">
        <h3 class="text-sm font-semibold">{{ $t('ui.gatewayApplicationStatus') }}</h3>
        <p v-if="statusError" class="text-sm text-destructive">{{ $message(statusError) }}</p>
        <p v-if="application.timedOut.value" class="text-sm">{{ $t('ui.gatewayApplicationIsNotCompleteSettingsAreSavedCheckStatus') }}</p>
        <p class="text-sm">{{ status?.applied ? $t('ui.gatewaySettingsApplied') : $t('ui.publicAccessStatusIsAwaitingConfirmation') }}<span v-if="status?.failure"> · {{ publicGatewayFailure(status.failure) }}</span></p>
        <p v-if="!status?.runtimes?.length" class="text-sm text-muted-foreground">{{ $t('ui.noPublicChallengePublications') }}</p>
        <div v-for="runtime in status?.runtimes ?? []" :key="runtime.runtimeId" class="flex min-w-0 flex-col gap-2 border-b pb-3">
          <span class="break-all font-mono text-xs">{{ runtime.runtimeId }}</span>
          <p v-if="runtime.failure" class="text-sm text-destructive">{{ publicGatewayFailure(runtime.failure) }}</p>
          <div v-for="endpoint in runtime.endpoints ?? []" :key="endpoint.containerPort" class="flex flex-wrap items-center gap-x-4 gap-y-2 text-sm">
            <span>{{ $t('ui.containerPort') }} <span class="font-mono tabular-nums">{{ endpoint.containerPort }}</span></span>
            <span>{{ $t('ui.directPort') }} <span class="font-mono tabular-nums">{{ endpoint.hostPort }}</span></span>
            <span>{{ $t('ui.publicPort') }} <span class="font-mono tabular-nums">{{ endpoint.publicPort ?? $t('ui.symbol') }}</span></span>
            <Badge :variant="endpoint.state === 'Ready' ? 'default' : 'outline'">{{ publicGatewayState(endpoint.state) }}</Badge>
            <span v-if="endpoint.failure" class="text-muted-foreground">{{ publicGatewayFailure(endpoint.failure) }}</span>
          </div>
        </div>
      </section>
    </template>
  </section>
</template>
