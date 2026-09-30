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
        <FieldLabel>{{ $t('ui.operatingEnvironmentType') }}</FieldLabel>
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
              <SelectItem v-for="option in kindOptions" :key="option.value" :value="option.value">
                {{ option.label }}
              </SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
      </Field>
      <Badge variant="secondary" class="mb-1 h-8 px-3">
        {{ runtime.allocation === RuntimeAllocation.Shared ? $t('ui.share') : $t('ui.eachTeamIsIndependent') }}
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
          {{ $t('ui.dynamicFlagInjection') }}
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
    <DefinitionSection v-else :title="$t('ui.operatingEnvironmentType')" :collapsible="false">
      <Field>
        <FieldLabel>{{ $t('ui.ovaSourceUrl') }}</FieldLabel>
        <Input v-model="runtime.definition.sourceUrl" :disabled="disabled" />
      </Field>
      <Field>
        <FieldLabel>{{ $t('ui.sha256') }}</FieldLabel>
        <Input v-model="runtime.definition.sha256" :disabled="disabled" />
      </Field>
    </DefinitionSection>

    <DefinitionSection :title="$t('ui.accessEntrance')" :collapsible="false" accent-title>
      <Field v-if="mode !== 'Ctf' && mode !== 'Awdp'">
        <FieldLabel>{{ $t('ui.flagSource') }}</FieldLabel>
        <Select
          :model-value="String(runtime.flagSource)"
          :disabled="disabled"
          @update:model-value="onUpdateModelValueRuntimeFlagSource"
        >
          <SelectTrigger class="w-full sm:max-w-xs"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectGroup>
              <SelectItem v-for="option in flagSourceOptions" :key="option.value" :value="String(option.value)">{{ option.label }}</SelectItem>
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
        <FieldLabel>{{ $t('ui.controlCheckEntry') }}</FieldLabel>
        <component :is="UrlBindingList"
          v-model="controlBindingList"
          :exposure-options="[{ value: UrlExposure.Participants, label: $t('ui.platformCheckUsage') }]"
          :show-service-name="hasServices"
          :service-names="serviceNames"
          :add-label="$t('ui.setUpControlCheckEntry')"
          :allow-custom-display="false"
          :disabled="disabled"
        />
      </Field>
    </DefinitionSection>

    <DefinitionSection :title="$t('ui.resourcesLifecycle')"  accent-title>
      <div class="grid gap-4 sm:grid-cols-3">
        <Field v-if="runtime.definition.kind === 'ova'">
          <FieldLabel>{{ $t('ui.memoryMib') }}</FieldLabel>
          <NullableNumberInput
            :model-value="bytesToMib(runtime.limits.memoryBytes)"
            :min="1"
            :placeholder="$t('ui.256')"
            :disabled="disabled"
            @update:model-value="onUpdateModelValueRuntimeLimitsMemoryBytes"
          />
        </Field>
        <Field v-if="runtime.definition.kind === 'ova'">
          <FieldLabel>{{ $t('ui.cpuCore') }}</FieldLabel>
          <NullableNumberInput
            :model-value="cpuMillicoresToCores(runtime.limits.cpuMillicores)"
            :min="0"
            step="0.1"
            :placeholder="$t('ui.05')"
            :disabled="disabled"
            @update:model-value="onUpdateModelValueRuntimeLimitsCpuMillicores"
          />
        </Field>
        <Field v-if="runtime.definition.kind === 'ova'">
          <FieldLabel>{{ $t('ui.maximumNumberOfProcesses') }}</FieldLabel>
          <NullableNumberInput
            :model-value="runtime.limits.pidsLimit"
            :min="1"
            :placeholder="$t('ui.128')"
            :disabled="disabled"
            @update:model-value="onUpdateModelValueRuntimeLimitsPidsLimit"
          />
        </Field>
        <Field>
          <FieldLabel>{{ $t('ui.instanceSurvivalTimeSeconds') }}</FieldLabel>
          <NullableNumberInput
            :model-value="runtime.ttlSeconds"
            :min="1"
            :max="604800"
            :placeholder="$t('ui.3600')"
            :disabled="disabled"
            @update:model-value="onUpdateModelValueRuntimeTtlSeconds"
          />
        </Field>
        <Field>
          <FieldLabel>{{ $t('ui.operationTimeoutSeconds') }}</FieldLabel>
          <NullableNumberInput
            :model-value="runtime.operationTimeoutSeconds"
            :min="1"
            :max="300"
            :placeholder="$t('ui.60')"
            :disabled="disabled"
            @update:model-value="onUpdateModelValueRuntimeOperationTimeoutSeconds"
          />
        </Field>
      </div>
    </DefinitionSection>
  </FieldGroup>
</template>
