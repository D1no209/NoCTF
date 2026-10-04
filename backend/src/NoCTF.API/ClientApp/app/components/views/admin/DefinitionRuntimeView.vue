<script setup lang="ts">
import { toRefs } from 'vue'
import type { DefinitionRuntimeViewState } from '~/features/admin/useDefinitionRuntime'

const viewProps = defineProps<{ state: DefinitionRuntimeViewState }>()
const { bytesToMib, cpuMillicoresToCores, RuntimeAllocation, UrlExposure, hasServices, showDynamicFlagInjection, dynamicFlagInjection, kindOptions, switchKind, flagSourceOptions, exposureOptions, controlBindingList, singleServiceOnly, serviceNames, model, DefinitionContainer, UrlBindingList, runtime, mode, disabled, onUpdateModelValueRuntimeLimitsMemoryBytes, onUpdateModelValueRuntimeLimitsCpuMillicores, onUpdateModelValueRuntimeLimitsPidsLimit, onUpdateModelValueRuntimeTtlSeconds, onUpdateModelValueRuntimeOperationTimeoutSeconds, onUpdateModelValueRuntimeFlagSource, setDynamicFlagInjection, onUpdateModelValueRuntimeUrlBindings } = toRefs(viewProps.state)
</script>

<template>
  <FieldGroup>
    <div class="flex flex-wrap items-end gap-4">
      <Field class="min-w-0 flex-1">
        <FieldLabel>{{ $t('runtime.label.operatingEnvironmentType') }}</FieldLabel>
        <Select
          :model-value="runtime.definition.kind"
          :disabled="disabled || mode === 'Awdp'"
          @update:model-value="switchKind(String($event))"
        >
          <SelectTrigger class="w-full">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectGroup>
              <SelectItem v-for="option in kindOptions" :key="option.value ?? undefined" :value="option.value">
                {{ option.label }}
              </SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
      </Field>
      <Badge variant="secondary" class="mb-1 h-8 px-3">
        {{ runtime.allocation === RuntimeAllocation.Shared ? $t('common.label.share') : $t('runtime.label.teamIndependent') }}
      </Badge>
    </div>

    <Field v-if="showDynamicFlagInjection" orientation="horizontal">
      <Switch
        id="ctf-dynamic-flag-injection"
        :model-value="dynamicFlagInjection"
        :disabled="disabled"
        @update:model-value="setDynamicFlagInjection($event === true)"
      />
      <div>
        <FieldLabel for="ctf-dynamic-flag-injection" class="font-normal">
          {{ $t('runtime.label.dynamicFlagInjection') }}
        </FieldLabel>
      </div>
    </Field>

    <component :is="DefinitionContainer"
      v-if="runtime.definition.kind === 'container'"
      :definition="runtime.definition"
      :model="model"
      :single-service-only="singleServiceOnly"
      :mode="mode"
      :flag-source="runtime.flagSource"
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
      <Field v-if="mode !== 'Ctf' && mode !== 'Awdp'">
        <FieldLabel>{{ $t('runtime.label.flagSource') }}</FieldLabel>
        <Select
          :model-value="String(runtime.flagSource)"
          :disabled="disabled"
          @update:model-value="onUpdateModelValueRuntimeFlagSource"
        >
          <SelectTrigger class="w-full sm:max-w-xs"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectGroup>
              <SelectItem v-for="option in flagSourceOptions" :key="option.value ?? undefined" :value="String(option.value)">{{ option.label }}</SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
      </Field>
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
      <Field v-if="mode === 'Koh'">
        <FieldLabel>{{ $t('runtime.label.controlCheckEntry') }}</FieldLabel>
        <component :is="UrlBindingList"
          v-model="controlBindingList"
          :exposure-options="[{ value: UrlExposure.Participants, label: $t('runtime.label.platformCheckUsage') }]"
          :show-service-name="hasServices"
          :service-names="serviceNames"
          :add-label="$t('runtime.definitionRuntime.label.setControlCheckEntry')"
          :allow-custom-display="false"
          :disabled="disabled"
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
          <FieldLabel>{{ $t('runtime.label.instanceSurvivalTimeSeconds') }}</FieldLabel>
          <NullableNumberInput
            :model-value="runtime.ttlSeconds"
            :min="1"
            :max="604800"
            :placeholder="$t('runtime.defaults.lifetimeSeconds')"
            :disabled="disabled"
            @update:model-value="onUpdateModelValueRuntimeTtlSeconds"
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
