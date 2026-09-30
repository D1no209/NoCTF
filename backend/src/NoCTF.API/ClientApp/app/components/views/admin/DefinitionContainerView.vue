<script setup lang="ts">
import { toRefs } from 'vue'
import type { DefinitionContainerViewState } from '~/features/admin/useDefinitionContainer'
const viewProps = defineProps<{ state: DefinitionContainerViewState }>()
const { definition, disabled, singleServiceOnly, showFlagEnvironmentVariable, serviceNameInputId, serviceImageInputId, selectedServiceIndex, selectedService, selectedServiceId, topology, advancedOpen, selectService, addService, canRemove, removeService, renameService, updateService } = toRefs(viewProps.state)
</script>

<template>
  <FieldGroup>
    <div class="flex flex-wrap items-center justify-between gap-2">
      <h3 class="text-sm font-semibold">{{ $t('ui.runtimeTopology') }}</h3>
      <div class="flex items-center gap-2">
        <Badge variant="secondary">{{ $t('ui.runtimeServiceCount', { count: definition.services.length }) }}</Badge>
        <Button v-if="!disabled && !singleServiceOnly" type="button" variant="outline" size="sm" :disabled="definition.services.length >= 64" @click="addService">{{ $t('ui.addRuntimeService') }}</Button>
      </div>
    </div>
    <ConnectionCanvas
      :nodes="topology.nodes"
      :edges="topology.edges"
      :selected-id="selectedServiceId"
      :label="$t('ui.runtimeTopology')"
      :source-label="$t('ui.runtimeServices')"
      :target-label="$t('ui.accessEntrance')"
      @select="selectService"
    />
    <FieldSet v-if="selectedService" :key="selectedServiceIndex">
      <FieldLegend class="flex w-full items-center justify-between gap-2 text-sm">
        <span class="truncate font-mono font-semibold">{{ selectedService.name || $t('ui.runtimeServices') }}</span>
        <Button v-if="!disabled && definition.services.length > 1" type="button" variant="ghost" size="sm" :aria-label="$t('ui.removeItem', { index: selectedServiceIndex + 1 })" :disabled="!canRemove(selectedServiceIndex)" @click="removeService(selectedServiceIndex)">{{ $t('ui.delete') }}</Button>
      </FieldLegend>
      <FieldGroup class="grid gap-4 sm:grid-cols-[minmax(0,1fr)_minmax(0,2fr)]">
        <Field>
          <FieldLabel :for="serviceNameInputId">{{ $t('ui.runtimeServiceName') }}</FieldLabel>
          <Input :id="serviceNameInputId" :model-value="selectedService.name" :disabled="disabled" @update:model-value="renameService(selectedServiceIndex, String($event ?? ''))" />
        </Field>
        <Field>
          <FieldLabel :for="serviceImageInputId">{{ $t('ui.mirror') }}</FieldLabel>
          <Input :id="serviceImageInputId" v-model="selectedService.image" :placeholder="$t('ui.registryExampleComChallengeLatest')" :disabled="disabled" />
        </Field>
      </FieldGroup>
      <DefinitionSection :title="$t('ui.advancedSettings')" :default-open="advancedOpen" accent-title>
        <FieldGroup class="grid gap-4 sm:grid-cols-2">
          <Field>
            <FieldLabel>{{ $t('ui.memoryMib') }}</FieldLabel>
            <NullableNumberInput v-model="selectedService.memoryMiB" :min="1" :disabled="disabled" />
          </Field>
          <Field>
            <FieldLabel>{{ $t('ui.cpuCore') }}</FieldLabel>
            <NullableNumberInput v-model="selectedService.cpuCores" :min="0.001" step="0.001" :disabled="disabled" />
          </Field>
        </FieldGroup>
        <Field>
          <FieldLabel>{{ $t('ui.startCommand') }}</FieldLabel>
          <StringListEditor :model-value="selectedService.command" :disabled="disabled" @update:model-value="updateService(selectedServiceIndex, 'command', $event)" />
        </Field>
        <Field>
          <FieldLabel>{{ $t('ui.runtimeArguments') }}</FieldLabel>
          <StringListEditor :model-value="selectedService.arguments" :disabled="disabled" @update:model-value="updateService(selectedServiceIndex, 'arguments', $event)" />
        </Field>
        <Field>
          <FieldLabel>{{ $t('ui.environmentVariables') }}</FieldLabel>
          <KeyValueEditor :model-value="selectedService.environment" :disabled="disabled" @update:model-value="updateService(selectedServiceIndex, 'environment', $event)" />
        </Field>
        <Field>
          <FieldLabel>{{ $t('ui.internalPort') }}</FieldLabel>
          <NumberListEditor :model-value="selectedService.internalPorts" :disabled="disabled" @update:model-value="updateService(selectedServiceIndex, 'internalPorts', $event)" />
        </Field>
        <Field v-if="showFlagEnvironmentVariable">
          <FieldLabel>{{ $t('ui.flagEnvironmentVariableName') }}</FieldLabel>
          <Input v-model="selectedService.flagEnvironmentVariableName" :disabled="disabled" />
        </Field>
      </DefinitionSection>
      <FieldDescription v-if="definition.services.length > 1 && !canRemove(selectedServiceIndex)">{{ $t('ui.runtimeServiceInUse') }}</FieldDescription>
    </FieldSet>
  </FieldGroup>
</template>
