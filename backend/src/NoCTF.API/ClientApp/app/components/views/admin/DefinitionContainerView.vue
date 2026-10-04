<script setup lang="ts">
import { toRefs } from 'vue'
import type { DefinitionContainerViewState } from '~/features/admin/useDefinitionContainer'
const viewProps = defineProps<{ state: DefinitionContainerViewState }>()
const { definition, disabled, singleServiceOnly, showFlagEnvironmentVariable, serviceNameInputId, serviceImageInputId, selectedServiceIndex, selectedService, selectedServiceId, topology, advancedOpen, selectService, addService, canRemove, removeService, renameService, updateService } = toRefs(viewProps.state)
</script>

<template>
  <FieldGroup>
    <div class="flex flex-wrap items-center justify-between gap-2">
      <h3 class="text-sm font-semibold">{{ $t('runtime.label.runtimeTopology') }}</h3>
      <div class="flex items-center gap-2">
        <Badge variant="secondary">{{ $t('runtime.label.runtimeServiceCount', { count: definition.services.length }) }}</Badge>
        <Button v-if="!disabled && !singleServiceOnly" type="button" variant="outline" size="sm" :disabled="definition.services.length >= 64" @click="addService">{{ $t('runtime.label.addRuntimeService') }}</Button>
      </div>
    </div>
    <ConnectionCanvas
      :nodes="topology.nodes"
      :edges="topology.edges"
      :selected-id="selectedServiceId"
      :label="$t('runtime.label.runtimeTopology')"
      :source-label="$t('runtime.label.runtimeServices')"
      :target-label="$t('runtime.label.accessEntrance')"
      @select="selectService"
    />
    <FieldSet v-if="selectedService" :key="selectedServiceIndex">
      <FieldLegend class="flex w-full items-center justify-between gap-2 text-sm">
        <span class="truncate font-mono font-semibold">{{ selectedService.name || $t('runtime.label.runtimeServices') }}</span>
        <Button v-if="!disabled && definition.services.length > 1" type="button" variant="ghost" size="sm" :aria-label="$t('common.label.removeItem', { index: selectedServiceIndex + 1 })" :disabled="!canRemove(selectedServiceIndex)" @click="removeService(selectedServiceIndex)">{{ $t('common.action.delete') }}</Button>
      </FieldLegend>
      <FieldGroup class="grid gap-4 sm:grid-cols-[minmax(0,1fr)_minmax(0,2fr)]">
        <Field>
          <FieldLabel :for="serviceNameInputId">{{ $t('runtime.label.runtimeServiceName') }}</FieldLabel>
          <Input :id="serviceNameInputId" :model-value="selectedService.name" :disabled="disabled" @update:model-value="renameService(selectedServiceIndex, String($event ?? ''))" />
        </Field>
        <Field>
          <FieldLabel :for="serviceImageInputId">{{ $t('administration.label.mirror') }}</FieldLabel>
          <Input :id="serviceImageInputId" v-model="selectedService.image" :placeholder="$t('administration.definitionContainer.label.registryExampleComChallenge')" :disabled="disabled" />
        </Field>
      </FieldGroup>
      <DefinitionSection :title="$t('administration.label.advancedSettings')" :default-open="advancedOpen" accent-title>
        <FieldGroup class="grid gap-4 sm:grid-cols-2">
          <Field>
            <FieldLabel>{{ $t('common.label.memoryMib') }}</FieldLabel>
            <NullableNumberInput v-model="selectedService.memoryMiB" :min="1" :disabled="disabled" />
          </Field>
          <Field>
            <FieldLabel>{{ $t('common.label.cpuCore') }}</FieldLabel>
            <NullableNumberInput v-model="selectedService.cpuCores" :min="0.001" step="0.001" :disabled="disabled" />
          </Field>
        </FieldGroup>
        <Field>
          <FieldLabel>{{ $t('administration.label.startCommand') }}</FieldLabel>
          <StringListEditor :model-value="selectedService.command" :disabled="disabled" @update:model-value="updateService(selectedServiceIndex, 'command', $event)" />
        </Field>
        <Field>
          <FieldLabel>{{ $t('runtime.label.runtimeArguments') }}</FieldLabel>
          <StringListEditor :model-value="selectedService.arguments" :disabled="disabled" @update:model-value="updateService(selectedServiceIndex, 'arguments', $event)" />
        </Field>
        <Field>
          <FieldLabel>{{ $t('administration.label.environmentVariables') }}</FieldLabel>
          <KeyValueEditor :model-value="selectedService.environment" :disabled="disabled" @update:model-value="updateService(selectedServiceIndex, 'environment', $event)" />
        </Field>
        <Field>
          <FieldLabel>{{ $t('administration.label.internalPort') }}</FieldLabel>
          <NumberListEditor :model-value="selectedService.internalPorts" :disabled="disabled" @update:model-value="updateService(selectedServiceIndex, 'internalPorts', $event)" />
        </Field>
        <Field v-if="showFlagEnvironmentVariable">
          <FieldLabel>{{ $t('administration.label.flagEnvironmentVariableName') }}</FieldLabel>
          <Input v-model="selectedService.flagEnvironmentVariableName" :disabled="disabled" />
        </Field>
      </DefinitionSection>
      <FieldDescription v-if="definition.services.length > 1 && !canRemove(selectedServiceIndex)">{{ $t('runtime.label.runtimeService') }}</FieldDescription>
    </FieldSet>
  </FieldGroup>
</template>
