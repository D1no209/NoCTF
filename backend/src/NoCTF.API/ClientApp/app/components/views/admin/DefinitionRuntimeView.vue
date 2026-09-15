<script setup lang="ts">
import { toRefs } from 'vue'
import type { DefinitionRuntimeViewState } from '~/features/admin/useDefinitionRuntime'

const viewProps = defineProps<{ state: DefinitionRuntimeViewState }>()
const { bytesToMib, coresToNanoCpus, mibToBytes, nanoCpusToCores, RuntimeAllocation, UrlExposure, isCompose, isPatchVerification, kindOptions, switchKind, flagSourceOptions, exposureOptions, controlBindingList, DefinitionCompose, DefinitionContainer, UrlBindingList, runtime, mode, disabled, onUpdateModelValueRuntimeLimitsMemoryBytes, onUpdateModelValueRuntimeLimitsNanoCpus, onUpdateModelValueRuntimeLimitsPidsLimit, onUpdateModelValueRuntimeTtlSeconds, onUpdateModelValueRuntimeOperationTimeoutSeconds, onUpdateModelValueRuntimeFlagSource, onUpdateModelValueRuntimeUrlBindings } = toRefs(viewProps.state)
</script>

<template>
  <FieldGroup>
    <div class="grid gap-4 sm:grid-cols-2">
      <Field>
        <FieldLabel>{{ $t('ui.distributionMethod') }}</FieldLabel>
        <div class="flex h-9 items-center gap-2">
          <Badge variant="secondary">
            {{ runtime.allocation === RuntimeAllocation.Shared ? $t('ui.share') : $t('ui.eachTeamIsIndependent') }}
          </Badge>
        </div>
        <FieldDescription v-if="mode === 'Koh'">{{ $t('ui.kohRequiresAllTeamsToShareTheSameEnvironment') }}</FieldDescription>
        <FieldDescription v-else>{{ $t('ui.requiresASeparateRuntimeEnvironmentForEachTeam', { mode }) }}</FieldDescription>
      </Field>
      <Field>
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
        <FieldDescription v-if="mode === 'Awdp'">{{ $t('ui.awdpOnlySupportsSingleContainerRunningEnvironments') }}</FieldDescription>
      </Field>
    </div>

    <component :is="DefinitionContainer"
      v-if="runtime.definition.kind === 'container'"
      :definition="runtime.definition"
      :mode="mode"
      :flag-source="runtime.flagSource"
      :disabled="disabled"
    />
    <component :is="DefinitionCompose"
      v-else
      :definition="runtime.definition"
      :disabled="disabled"
    />

    <DefinitionSection :title="$t('ui.resourcesLifecycle')"  :collapsible="false">
      <div class="grid gap-4 sm:grid-cols-3">
        <Field>
          <FieldLabel>{{ $t('ui.memoryMib') }}</FieldLabel>
          <NullableNumberInput
            :model-value="bytesToMib(runtime.limits.memoryBytes)"
            :min="1"
            :placeholder="$t('ui.256')"
            :disabled="disabled"
            @update:model-value="onUpdateModelValueRuntimeLimitsMemoryBytes"
          />
        </Field>
        <Field>
          <FieldLabel>{{ $t('ui.cpuCore') }}</FieldLabel>
          <NullableNumberInput
            :model-value="nanoCpusToCores(runtime.limits.nanoCpus)"
            :min="0"
            step="0.1"
            :placeholder="$t('ui.05')"
            :disabled="disabled"
            @update:model-value="onUpdateModelValueRuntimeLimitsNanoCpus"
          />
        </Field>
        <Field>
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
      <FieldDescription>{{ $t('ui.blankValuesUseThePlatformDefaultResourceLimitsAndLifecycle') }}</FieldDescription>
    </DefinitionSection>

    <DefinitionSection :title="$t('ui.flagAccess')"  :collapsible="false">
      <Field v-if="mode !== 'Ctf' && mode !== 'Awdp'">
        <FieldLabel>{{ $t('ui.flagSource') }}</FieldLabel>
        <Select
          :model-value="String(runtime.flagSource)"
          :disabled="disabled"
          @update:model-value="onUpdateModelValueRuntimeFlagSource"
        >
          <SelectTrigger class="w-full sm:max-w-xs">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectGroup>
              <SelectItem v-for="option in flagSourceOptions" :key="option.value" :value="String(option.value)">
                {{ option.label }}
              </SelectItem>
            </SelectGroup>
          </SelectContent>
        </Select>
      </Field>
      <Field v-else>
        <FieldLabel>{{ $t('ui.flagSource') }}</FieldLabel>
        <div class="flex h-9 items-center gap-2">
          <Badge variant="secondary">
            {{ isPatchVerification ? $t('ui.notRequired') : $t('ui.perTeamFlagEnvironmentInjection') }}
          </Badge>
        </div>
        <FieldDescription>
          {{ isPatchVerification
            ? $t('ui.patchVerificationTargetsDoNotReceiveFlags')
            : $t('ui.forRuntimeChallengesThePlatformGeneratesATeamSpecificFlag') }}
        </FieldDescription>
      </Field>

      <Field>
        <FieldLabel>{{ $t('ui.accessEntrance') }}</FieldLabel>
        <component :is="UrlBindingList"
          :model-value="runtime.urlBindings"
          :exposure-options="exposureOptions"
          :show-service-name="isCompose"
          :disabled="disabled"
          @update:model-value="onUpdateModelValueRuntimeUrlBindings"
        />
        <FieldDescription v-if="mode === 'Koh'"> {{ $t('ui.kohRequiresAtLeastOneEntranceVisibleToAllParticipants') }} </FieldDescription>
        <FieldDescription v-else-if="mode === 'Awd'"> {{ $t('ui.inAwdPlayersAccessEachOtherSServicesUsuallyRequiring') }} </FieldDescription>
        <FieldDescription v-else-if="mode === 'Awdp'"> {{ $t('ui.awdpAttackRuntimeAccessIsVisibleOnlyToItsOwning') }} </FieldDescription>
      </Field>

      <Field v-if="mode === 'Koh'">
        <FieldLabel>{{ $t('ui.controlCheckEntry') }}</FieldLabel>
        <component :is="UrlBindingList"
          v-model="controlBindingList"
          :exposure-options="[{ value: UrlExposure.Participants, label: $t('ui.platformCheckUsage') }]"
          :show-service-name="isCompose"
          :add-label="$t('ui.setUpControlCheckEntry')"
          :allow-custom-display="false"
          :disabled="disabled"
        />
        <FieldDescription>{{ $t('ui.thePlatformPeriodicallyChecksTheControlAddressItIsRequired') }}</FieldDescription>
      </Field>
    </DefinitionSection>
  </FieldGroup>
</template>
