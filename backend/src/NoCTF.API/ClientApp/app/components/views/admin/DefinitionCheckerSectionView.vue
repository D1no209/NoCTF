<script setup lang="ts">
import { toRefs } from 'vue'
import type { DefinitionCheckerSectionViewState } from '~/features/admin/useDefinitionCheckerSection'

const viewProps = defineProps<{ state: DefinitionCheckerSectionViewState }>()
const { isCompose, toggleChecker, toggleCheckerJob, RunnerJobEditor, model, mode, disabled } = toRefs(viewProps.state)
</script>

<template>
  <FieldSet v-if="mode === 'Awd'" class="rounded-md border p-4">
    <FieldLegend class="px-1 text-sm font-medium">{{ $t('ui.checkerServiceHealthCheck') }}</FieldLegend>
    <Field orientation="horizontal">
      <Switch
        id="def-has-checker"
        :model-value="model.checker !== null"
        :disabled="disabled"
        @update:model-value="toggleChecker($event === true)"
      />
      <FieldLabel for="def-has-checker" class="font-normal">{{ $t('ui.enablePeriodicServiceChecks') }}</FieldLabel>
    </Field>
    <template v-if="model.checker">
      <component :is="RunnerJobEditor" :job="model.checker.job" :disabled="disabled" />
      <Field v-if="isCompose">
        <FieldLabel>{{ $t('ui.targetServiceName') }}</FieldLabel>
        <Input
          v-model="model.checker.targetServiceName"
          :placeholder="$t('ui.serviceNameInCompose')"
          class="font-mono text-sm"
          :disabled="disabled"
        />
        <FieldDescription>{{ $t('ui.theComposeOperatingEnvironmentMustSpecifyTheServiceToBe') }}</FieldDescription>
      </Field>
    </template>
  </FieldSet>

  <FieldSet v-else-if="mode === 'Awdp' || mode === 'Ctf'" class="rounded-md border p-4">
    <FieldLegend class="px-1 text-sm font-medium">{{ $t('ui.oneShotFixVerificationChecker') }}</FieldLegend>
    <Field orientation="horizontal">
      <Switch
        id="def-has-checker-job"
        :model-value="model.checkerJob !== null"
        :disabled="disabled"
        @update:model-value="toggleCheckerJob($event === true)"
      />
      <FieldLabel for="def-has-checker-job" class="font-normal">{{ $t('ui.enableTheOneShotFixVerificationChecker') }}</FieldLabel>
    </Field>
    <template v-if="model.checkerJob">
      <component :is="RunnerJobEditor" :job="model.checkerJob" :disabled="disabled" />
      <Field orientation="horizontal">
        <Switch
          id="def-checker-fix-input"
          v-model="model.checkerFixInput"
          :disabled="disabled"
        />
        <div class="grid gap-1">
          <FieldLabel for="def-checker-fix-input" class="font-normal">{{ $t('ui.provideTheFixPackageToTheChecker') }}</FieldLabel>
          <FieldDescription>
            {{ $t('ui.beforeItStartsTheCheckerReceivesThePlatformValidatedFix') }}
          </FieldDescription>
        </div>
      </Field>
    </template>
    <FieldDescription v-else>
      {{ $t('ui.enableTheOneShotFixCheckerBeforeProvidingItWith') }}
    </FieldDescription>
  </FieldSet>

  <FieldGroup v-if="(mode === 'Awd' && model.checker) || ((mode === 'Awdp' || mode === 'Ctf') && model.checkerJob)">
    <Field orientation="horizontal" :data-disabled="disabled">
      <Switch id="def-checker-allow-root" v-model="model.checkerAllowRoot" :disabled="disabled" />
      <FieldContent>
        <FieldLabel for="def-checker-allow-root">{{ $t('ui.allowTheCheckerToRunAsRoot') }}</FieldLabel>
        <FieldDescription>
          {{ $t('ui.offByDefaultTheImageMustDeclareANonzeroNumeric') }}
          {{ $t('ui.enableOnlyForTrustedCheckersNetworkIsolationCapabilityLimitsAnd') }}
        </FieldDescription>
      </FieldContent>
    </Field>
  </FieldGroup>
</template>
