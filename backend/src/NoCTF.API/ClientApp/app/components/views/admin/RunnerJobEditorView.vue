<script setup lang="ts">
import { toRefs } from 'vue'
import type { RunnerJobEditorViewState } from '~/features/admin/useRunnerJobEditor'

const viewProps = defineProps<{ state: RunnerJobEditorViewState }>()
const { job, disabled, onUpdateModelValueJobCommand, onUpdateModelValueJobEnvironment, onUpdateModelValueJobTimeoutSeconds } = toRefs(viewProps.state)
</script>

<template>
  <FieldGroup>
    <Field>
      <FieldLabel>{{ $t('ui.mirror') }}</FieldLabel>
      <Input v-model="job.image" :placeholder="$t('ui.registryExampleComCheckerLatest')" class="font-mono text-sm" :disabled="disabled" />
    </Field>
    <Field>
      <FieldLabel>{{ $t('ui.startCommand') }}</FieldLabel>
      <StringListEditor
        :model-value="job.command"
        :placeholder="$t('ui.parametersSuchAsCheck')"
        :add-label="$t('ui.addParameters')"
        :disabled="disabled"
        @update:model-value="onUpdateModelValueJobCommand"
      />
      <FieldDescription>{{ $t('ui.leaveItBlankToUseTheDefaultEntryOfThe') }}</FieldDescription>
    </Field>
    <Field>
      <FieldLabel>{{ $t('ui.environmentVariables') }}</FieldLabel>
      <KeyValueEditor
        :model-value="job.environment"
        :key-placeholder="$t('ui.variableName')"
        :value-placeholder="$t('ui.value')"
        :add-label="$t('ui.addEnvironmentVariables')"
        :disabled="disabled"
        @update:model-value="onUpdateModelValueJobEnvironment"
      />
      <FieldDescription>{{ $t('ui.variableNamesPrefixedWithNoctfAreNotAllowedPlatformReserved') }}</FieldDescription>
    </Field>
    <Field>
      <FieldLabel>{{ $t('ui.timeoutSeconds') }}</FieldLabel>
      <NullableNumberInput
        :model-value="job.timeoutSeconds"
        :min="1"
        :max="1800"
        :placeholder="$t('ui.default60')"
        :disabled="disabled"
        @update:model-value="onUpdateModelValueJobTimeoutSeconds"
      />
    </Field>
  </FieldGroup>
</template>
