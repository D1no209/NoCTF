<script setup lang="ts">
import { toRefs } from 'vue'
import type { DefinitionCheckerSectionViewState } from '~/features/admin/useDefinitionCheckerSection'
const props = defineProps<{ state: DefinitionCheckerSectionViewState }>()
const { model, disabled, serviceNames, requirements, setRequirement, setReadyTimeout, RunnerJobEditor } = toRefs(props.state)
</script>
<template>
  <FieldSet v-if="model.checker">
    <FieldLegend>{{ $t('administration.capability.check') }}</FieldLegend>
    <component :is="RunnerJobEditor" :job="model.checker.job" :disabled="disabled" />
    <Field><FieldLabel>{{ $t('administration.label.targetServiceName') }}</FieldLabel>
      <Select v-model="model.checker.targetServiceName" :disabled="disabled"><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem v-for="name in serviceNames" :key="name" :value="name">{{ name }}</SelectItem></SelectContent></Select>
    </Field>
    <Field v-for="requirement in requirements" :key="requirement.kind" orientation="horizontal">
      <Checkbox :id="`check-${requirement.kind}`" :model-value="model.checkRequirements.includes(requirement.kind)" :disabled="disabled" @update:model-value="setRequirement(requirement.kind, $event === true)" />
      <FieldLabel :for="`check-${requirement.kind}`">{{ $t(requirement.label) }}</FieldLabel>
    </Field>
    <Field><FieldLabel>{{ $t('administration.label.readyTimeoutSeconds') }}</FieldLabel><NullableNumberInput :model-value="model.readyTimeoutSeconds" :min="1" :disabled="disabled" @update:model-value="setReadyTimeout" /></Field>
    <Field orientation="horizontal"><Switch id="checker-fix-input" v-model="model.checkerFixInput" :disabled="disabled" /><FieldLabel for="checker-fix-input">{{ $t('administration.definitionChecker.description.fixPackageChecker') }}</FieldLabel></Field>
  </FieldSet>
</template>
