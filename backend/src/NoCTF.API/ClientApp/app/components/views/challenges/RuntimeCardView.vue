<script setup lang="ts">
import { toRefs } from 'vue'
import type { RuntimeCardViewState } from '~/features/challenges/useRuntimeCard'

const viewProps = defineProps<{ state: RuntimeCardViewState }>()
const { runnerFailureLabel, canStop, stopDisabled, runtime, loading, loadError, extendMinutes, polling, timedOut, retryLoad, start, stop, reset, extend, ttl, isRunning, busy, stateVariant, RuntimeAccessUrl, controls, dockTarget } = toRefs(viewProps.state)
</script>

<template>
  <Teleport defer :to="dockTarget || 'body'" :disabled="!dockTarget">
  <section class="flex flex-col gap-4" aria-labelledby="runtime-card-title">
    <header class="flex items-center justify-between gap-2">
        <h3 id="runtime-card-title" class="text-sm font-semibold">{{ $t('ui.questionEnvironment') }}</h3>
        <Badge v-if="runtime" :variant="stateVariant">
          {{ runtimeStateLabel(runtime.state) }}
        </Badge>
    </header>
    <div class="flex flex-col gap-4">
      <Skeleton v-if="loading" class="h-16 w-full" />

      <template v-else>
        <Alert v-if="loadError" variant="destructive">
          <AlertDescription class="flex flex-wrap items-center justify-between gap-2">
            <span>{{ $message(loadError) }}</span>
            <Button size="sm" variant="outline" @click="retryLoad">{{ $t('ui.retry') }}</Button>
          </AlertDescription>
        </Alert>

        <Alert v-else-if="timedOut">
          <AlertDescription>{{ $t('ui.theEnvironmentStatusUpdateTimedOutPleaseRefreshManuallyLater') }}</AlertDescription>
        </Alert>

        <template v-if="!loadError && runtime">
          <div v-if="isRunning && runtime.accesses?.length" class="flex flex-col gap-1">
            <span class="text-sm text-muted-foreground">{{ $t('ui.accessAddress') }}</span>
            <component :is="RuntimeAccessUrl"
              v-for="access in runtime.accesses"
              :key="`${access.directAddress}:${access.webSocketAddress}`"
              :access="access"
            />
          </div>

          <div v-if="isRunning && ttl" class="text-sm"> {{ $t('ui.timeRemaining') }}<span class="font-mono font-medium tabular-nums">{{ ttl }}</span>
          </div>
        </template>

        <FieldDescription v-if="runtime?.waitingReason">{{ runnerFailureLabel(runtime.waitingReason) }}</FieldDescription>

        <div v-if="!loadError" class="flex flex-wrap items-center gap-2">
          <template v-if="controls === 'full'">
            <Button v-if="!runtime || runtime.state === 'Stopped' || runtime.state === 'Failed'" :disabled="busy" @click="start">
              <Spinner v-if="busy && polling" data-icon="inline-start" /> {{ $t('ui.startEnvironment') }} </Button>
            <Button v-if="canStop" variant="outline" :disabled="stopDisabled" @click="stop"> {{ $t('ui.stop') }} </Button>
          </template>
          <Button v-if="runtime && controls !== 'readonly'" variant="outline" :disabled="busy" @click="reset">
            <Spinner v-if="polling" data-icon="inline-start" /> {{ $t('ui.resetEnvironment') }} </Button>
          <template v-if="controls === 'full' && isRunning">
            <div class="flex items-center gap-2">
              <NumberInput
                v-model.number="extendMinutes"

                min="1"
                max="720"
                class="w-20"
                :aria-label="$t('ui.renewalMinutes2')"
              />
              <Button variant="outline" :disabled="busy" @click="extend">{{ $t('ui.renewalMinutes') }}</Button>
            </div>
          </template>
        </div>
      </template>
    </div>
  </section>
  </Teleport>
</template>
