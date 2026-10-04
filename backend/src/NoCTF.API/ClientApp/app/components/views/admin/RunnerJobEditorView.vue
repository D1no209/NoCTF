<script setup lang="ts">
import { toRefs } from 'vue'
import type { RunnerJobEditorViewState } from '~/features/admin/useRunnerJobEditor'

const viewProps = defineProps<{ state: RunnerJobEditorViewState }>()
const { job, disabled, onUpdateModelValueJobCommand, onUpdateModelValueJobEnvironment, onUpdateModelValueJobTimeoutSeconds } = toRefs(viewProps.state)
</script>

<template>
  <FieldGroup>
    <Field>
      <FieldLabel>{{ $t('administration.label.mirror') }}</FieldLabel>
      <Input v-model="job.image" :placeholder="$t('administration.runnerJob.label.registryExampleComChecker')" class="font-mono text-sm" :disabled="disabled" />
    </Field>
    <Field>
      <FieldLabel>{{ $t('administration.label.startCommand') }}</FieldLabel>
      <StringListEditor
        :model-value="job.command"
        :placeholder="$t('administration.label.parametersCheck')"
        :add-label="$t('administration.label.addParameters')"
        :disabled="disabled"
        @update:model-value="onUpdateModelValueJobCommand"
      />
      <FieldDescription>{{ $t('administration.runnerJob.description.leaveBlankDefaultEntry') }}</FieldDescription>
    </Field>
    <Field>
      <FieldLabel>{{ $t('administration.label.environmentVariables') }}</FieldLabel>
      <KeyValueEditor
        :model-value="job.environment"
        :key-placeholder="$t('administration.label.variableName')"
        :value-placeholder="$t('common.label.valueEditor')"
        :add-label="$t('administration.label.addEnvironmentVariables')"
        :disabled="disabled"
        @update:model-value="onUpdateModelValueJobEnvironment"
      />
      <FieldDescription>{{ $t('administration.runnerJob.description.variableNamesPrefixedNoctf') }}</FieldDescription>
    </Field>
    <Field>
      <FieldLabel>{{ $t('administration.label.timeoutSeconds') }}</FieldLabel>
      <NullableNumberInput
        :model-value="job.timeoutSeconds"
        :min="1"
        :max="1800"
        :placeholder="$t('administration.label.default.jobEditorView')"
        :disabled="disabled"
        @update:model-value="onUpdateModelValueJobTimeoutSeconds"
      />
    </Field>
  </FieldGroup>
</template>
