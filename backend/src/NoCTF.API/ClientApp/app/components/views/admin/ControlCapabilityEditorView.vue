<script setup lang="ts">
import { toRefs } from 'vue'
import type { ControlCapabilityEditorViewState } from '~/features/admin/useControlCapabilityEditor'
const props = defineProps<{ state: ControlCapabilityEditorViewState }>()
const { model, disabled, serviceNames, RunnerJobEditor } = toRefs(props.state)
</script>
<template>
  <FieldSet v-if="model.control">
    <FieldLegend>{{ $t('administration.capability.control') }}</FieldLegend>
    <component :is="RunnerJobEditor" :job="model.control.job" :disabled="disabled" />
    <Field v-if="serviceNames.length"><FieldLabel>{{ $t('administration.label.targetServiceName') }}</FieldLabel>
      <Select v-model="model.control.serviceName" :disabled="disabled"><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem v-for="name in serviceNames" :key="name" :value="name">{{ name }}</SelectItem></SelectContent></Select>
    </Field>
  </FieldSet>
</template>
