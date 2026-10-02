<script setup lang="ts">
import { toRefs } from 'vue'
import type { ChallengeTestRuntimePanelViewState } from '~/features/admin/useChallengeTestRuntimePanel'

const viewProps = defineProps<{ state: ChallengeTestRuntimePanelViewState }>()
const { Check, Clipboard, FlaskConical, RefreshCw, runtime, loading, loadError, copied, extendMinutes, extendMinutesInvalid, renewalTooEarly, validExtension, timedOut, retryLoad, start, stop, reset, extend, copyTestFlag, active, canStart, busy, ttl, canExtend, stateVariant, flagVariant, flagStateLabel, RuntimeFlagsPanel, RuntimeAccessUrl, definitionDirty } = toRefs(viewProps.state)
</script>

<template>
  <section class="mt-2 flex flex-col gap-3 pt-4" aria-labelledby="challenge-test-runtime-title">
    <Separator />
    <header class="flex flex-wrap items-start justify-between gap-3">
      <div class="flex items-start gap-3">
        <span class="mt-0.5 flex size-9 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
          <FlaskConical class="size-4" aria-hidden="true" />
        </span>
        <div>
          <h3 id="challenge-test-runtime-title" class="text-base font-semibold">{{ $t('ui.challengeTestContainer') }}</h3>
        </div>
      </div>
      <div class="flex items-center gap-2">
        <Badge v-if="runtime" :variant="stateVariant">{{ runtimeStateLabel(runtime.state) }}</Badge>
        <Button type="button" variant="outline" size="sm" :disabled="loading || busy" @click="retryLoad">
          <RefreshCw data-icon="inline-start" />{{ $t('ui.refreshStatus') }}
        </Button>
      </div>
    </header>

    <Alert v-if="definitionDirty">
      <AlertDescription>{{ $t('ui.theRuntimeDefinitionHasUnsavedChangesSaveThemBeforeStarting') }}</AlertDescription>
    </Alert>
    <Skeleton v-if="loading" class="h-28 w-full" />
    <Alert v-else-if="loadError" variant="destructive">
      <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
        <span>{{ $message(loadError) }}</span>
        <Button type="button" variant="outline" size="sm" @click="retryLoad">{{ $t('ui.retry') }}</Button>
      </AlertDescription>
    </Alert>
    <Alert v-else-if="timedOut" variant="destructive">
      <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
        <span>{{ $t('ui.testContainerStatusUpdatesTimedOutRefreshManually') }}</span>
        <Button type="button" variant="outline" size="sm" @click="retryLoad">{{ $t('ui.reload') }}</Button>
      </AlertDescription>
    </Alert>

    <template v-if="!loading && !loadError">
      <div v-if="runtime" class="grid gap-3 text-sm sm:grid-cols-2">
        <div class="grid gap-1">
          <span class="text-muted-foreground">{{ $t('ui.placement') }}</span>
          <span>{{ enumLabel(RuntimeKindLabel, runtime.runtimeKind) }} · {{ enumLabel(RuntimeProviderLabel, runtime.provider) }}</span>
        </div>
        <div class="grid gap-1">
          <span class="text-muted-foreground">{{ $t('ui.dynamicFlag') }}</span>
          <Badge :variant="flagVariant" class="w-fit">
            <Spinner v-if="runtime.flagState === 'Pending'" class="size-3" />
            {{ flagStateLabel(runtime.flagState) }}
          </Badge>
        </div>
        <div v-if="runtime.state === 'Failed' && runtime.failureCode" class="grid gap-1 sm:col-span-2">
          <span class="text-muted-foreground">{{ $t('ui.failureReason') }}</span>
          <span class="text-destructive">{{ enumLabel(RuntimeFailureCodeLabel, runtime.failureCode) }}</span>
        </div>
        <div v-if="runtime.state === 'Running' && runtime.accesses?.length" class="grid gap-2 sm:col-span-2">
          <span class="text-muted-foreground">{{ $t('ui.accessAddress') }}</span>
          <component :is="RuntimeAccessUrl" v-for="access in runtime.accesses" :key="`${access.directAddress}:${access.webSocketAddress}`" :access="access" />
        </div>
        <div v-if="runtime.testFlag" class="grid gap-2 sm:col-span-2">
          <span class="text-muted-foreground">{{ $t('ui.testFlagForThisInstance') }}</span>
          <div class="flex min-w-0 items-center gap-2">
            <ScrollSurface as="code" axis="x" class="min-w-0 flex-1 overflow-x-auto border bg-muted px-3 py-2 font-mono text-xs">{{ runtime.testFlag }}</ScrollSurface>
            <Button type="button" variant="outline" size="sm" @click="copyTestFlag">
              <Check v-if="copied" data-icon="inline-start" />
              <Clipboard v-else data-icon="inline-start" />
              {{ copied ? $t('ui.copied') : $t('ui.copy') }}
            </Button>
          </div>
        </div>
        <div v-if="runtime.state === 'Running' && ttl" class="sm:col-span-2">
          {{ $t('ui.timeRemaining') }}<span class="font-mono font-medium tabular-nums">{{ ttl }}</span>
        </div>
      </div>
      <p v-else class="text-sm text-muted-foreground">{{ $t('ui.noTestContainerHasBeenCreatedYet') }}</p>

      <div class="flex flex-wrap items-center gap-2">
        <Button
          v-if="canStart"
          type="button"
          :disabled="busy || definitionDirty"
          @click="start"
        >
          <Spinner v-if="busy" data-icon="inline-start" />{{ $t('ui.startTestContainer') }}
        </Button>
        <Button v-else-if="runtime?.state === 'Stopping'" type="button" disabled>
          <Spinner data-icon="inline-start" />{{ $t('ui.stopping') }}
        </Button>
        <Hint v-if="active" :content="$t('ui.stoppingATestInstanceRemovesItsContainerAndNetworkBut')">
          <Button type="button" variant="outline" :disabled="busy" @click="stop">
            {{ $t('ui.stop') }}
          </Button>
        </Hint>
        <Button
          v-if="active"
          type="button"
          variant="outline"
          :disabled="busy || definitionDirty"
          @click="reset"
        >
          <Spinner v-if="busy" data-icon="inline-start" />{{ $t('ui.resetTestContainer') }}
        </Button>
        <div v-if="canExtend" class="flex flex-col gap-1">
          <div class="flex items-start gap-2">
          <Field class="w-36 shrink-0" :data-invalid="extendMinutesInvalid">
            <FieldLabel :for="`test-runtime-extend-${runtime?.id}`" class="sr-only">{{ $t('ui.renewalMinutes2') }}</FieldLabel>
            <NumberInput :id="`test-runtime-extend-${runtime?.id}`" v-model="extendMinutes"
              min="1" max="1440" required class="w-full" :aria-invalid="extendMinutesInvalid" />
            <FieldDescription v-if="extendMinutesInvalid">{{ $t('ui.renewalMinutesRange', { max: 1440 }) }}</FieldDescription>
          </Field>
          <Button type="button" variant="outline" :disabled="!validExtension" @click="extend">{{ $t('ui.renewalMinutes') }}</Button>
          </div>
          <FieldDescription v-if="renewalTooEarly">{{ $t('ui.renewalAvailableInFinalTenMinutes') }}</FieldDescription>
        </div>
      </div>
      <component :is="RuntimeFlagsPanel" v-if="runtime?.id" :key="runtime.id" :runtime-id="runtime.id" />
    </template>
  </section>
</template>
