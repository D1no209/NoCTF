<script setup lang="ts">
import { toRefs } from 'vue'
import type { ControlCapabilityEditorViewState } from '~/features/admin/useControlCapabilityEditor'
const props = defineProps<{ state: ControlCapabilityEditorViewState }>()
const { model, disabled, serviceNames, setPort, setTimeout } = toRefs(props.state)
</script>
<template>
  <FieldSet v-if="model.control">
    <FieldLegend>{{ $t('administration.capability.control') }}</FieldLegend>
    <Field><FieldLabel>{{ $t('administration.capability.controlUrl') }}</FieldLabel><Input v-model="model.control.urlTemplate" :disabled="disabled" /></Field>
    <Field v-if="serviceNames.length"><FieldLabel>{{ $t('administration.label.targetServiceName') }}</FieldLabel>
      <Select v-model="model.control.serviceName" :disabled="disabled"><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem v-for="name in serviceNames" :key="name" :value="name">{{ name }}</SelectItem></SelectContent></Select>
    </Field>
    <Field><FieldLabel>{{ $t('administration.capability.controlPort') }}</FieldLabel><NullableNumberInput :model-value="model.control.containerPort ?? null" :min="1" :max="65535" :disabled="disabled" @update:model-value="setPort" /></Field>
    <Field><FieldLabel>{{ $t('administration.capability.timeout') }}</FieldLabel><NullableNumberInput :model-value="model.control.timeoutSeconds ?? null" :min="1" :max="3600" :disabled="disabled" @update:model-value="setTimeout" /></Field>
  </FieldSet>
</template>
