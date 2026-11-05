<script setup lang="ts">
import { toRefs } from 'vue'
import type { DefinitionRuntimeViewState } from '~/features/admin/useDefinitionRuntime'

const viewProps = defineProps<{ state: DefinitionRuntimeViewState }>()
const { bytesToMib, cpuMillicoresToCores, hasServices, kindOptions, switchKind, exposureOptions, serviceNames, model, DefinitionContainer, UrlBindingList, runtime, disabled, onUpdateModelValueRuntimeLimitsMemoryBytes, onUpdateModelValueRuntimeLimitsCpuMillicores, onUpdateModelValueRuntimeLimitsPidsLimit, onUpdateModelValueRuntimeOperationTimeoutSeconds, onUpdateModelValueRuntimeUrlBindings, egressPolicyOptions, onUpdateModelValueRuntimeEgressPolicy } = toRefs(viewProps.state)
</script>

<template>
  <FieldGroup>
    <div class="flex flex-wrap items-end gap-4">
      <Field class="min-w-0 flex-1">
        <FieldLabel>{{ $t('runtime.label.operatingEnvironmentType') }}</FieldLabel>
        <Select
          :model-value="runtime.definition.kind"
          :disabled="disabled"
          @update:model-value="switchKind(String($event))"
        >
          <SelectTrigger class="w-full">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectGroup>
              <SelectItem v-for="option in kindOptions" :key="option.value" :value="option.value">
                {{ option.label }}
              </SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
      </Field>
    </div>

    <Field>
      <FieldLabel>{{ $t('runtime.label.egressPolicy') }}</FieldLabel>
      <Select
        :model-value="runtime.egressPolicy"
        :disabled="disabled"
        @update:model-value="onUpdateModelValueRuntimeEgressPolicy"
      >
        <SelectTrigger class="w-full sm:max-w-xs"><SelectValue /></SelectTrigger>
        <SelectContent>
          <SelectGroup>
            <SelectItem v-for="option in egressPolicyOptions" :key="option.value" :value="String(option.value)">{{ option.label }}</SelectItem>
          </SelectGroup>
        </SelectContent>
      </Select>
      <FieldDescription>{{ $t('runtime.description.egressPolicy') }}</FieldDescription>
    </Field>

    <component :is="DefinitionContainer"
      v-if="runtime.definition.kind === 'container'"
      :definition="runtime.definition"
      :model="model"
      :single-service-only="false"
      :disabled="disabled"
    />
    <DefinitionSection v-else :title="$t('runtime.label.operatingEnvironmentType')" :collapsible="false">
      <Field>
        <FieldLabel>{{ $t('administration.label.ovaSourceUrl') }}</FieldLabel>
        <Input v-model="runtime.definition.sourceUrl" :disabled="disabled" />
      </Field>
      <Field>
        <FieldLabel>{{ $t('administration.label.sha') }}</FieldLabel>
        <Input v-model="runtime.definition.sha256" :disabled="disabled" />
      </Field>
    </DefinitionSection>

    <DefinitionSection :title="$t('runtime.label.accessEntrance')" :collapsible="false" accent-title>
      <Field>
        <component :is="UrlBindingList"
          :model-value="runtime.urlBindings"
          :exposure-options="exposureOptions"
          :show-service-name="hasServices"
          :service-names="serviceNames"
          :disabled="disabled"
          @update:model-value="onUpdateModelValueRuntimeUrlBindings"
        />
      </Field>
    </DefinitionSection>

    <DefinitionSection :title="$t('runtime.label.resourcesLifecycle')"  accent-title>
      <div class="grid gap-4 sm:grid-cols-3">
        <Field v-if="runtime.definition.kind === 'ova'">
          <FieldLabel>{{ $t('common.label.memoryMib') }}</FieldLabel>
          <NullableNumberInput
            :model-value="bytesToMib(runtime.limits.memoryBytes)"
            :min="1"
            :placeholder="$t('runtime.resources.memoryPlaceholder')"
            :disabled="disabled"
            @update:model-value="onUpdateModelValueRuntimeLimitsMemoryBytes"
          />
        </Field>
        <Field v-if="runtime.definition.kind === 'ova'">
          <FieldLabel>{{ $t('common.label.cpuCore') }}</FieldLabel>
          <NullableNumberInput
            :model-value="cpuMillicoresToCores(runtime.limits.cpuMillicores)"
            :min="0"
            step="0.1"
            :placeholder="$t('runtime.resources.cpuPlaceholder')"
            :disabled="disabled"
            @update:model-value="onUpdateModelValueRuntimeLimitsCpuMillicores"
          />
        </Field>
        <Field v-if="runtime.definition.kind === 'ova'">
          <FieldLabel>{{ $t('common.label.maximumNumberProcesses') }}</FieldLabel>
          <NullableNumberInput
            :model-value="runtime.limits.pidsLimit"
            :min="1"
            :placeholder="$t('runtime.resources.pidPlaceholder')"
            :disabled="disabled"
            @update:model-value="onUpdateModelValueRuntimeLimitsPidsLimit"
          />
        </Field>
        <Field>
          <FieldLabel>{{ $t('runtime.label.timeoutSeconds') }}</FieldLabel>
          <NullableNumberInput
            :model-value="runtime.operationTimeoutSeconds"
            :min="1"
            :max="300"
            :placeholder="$t('runtime.defaults.checkerTimeoutSeconds')"
            :disabled="disabled"
            @update:model-value="onUpdateModelValueRuntimeOperationTimeoutSeconds"
          />
        </Field>
      </div>
    </DefinitionSection>
  </FieldGroup>
</template>
