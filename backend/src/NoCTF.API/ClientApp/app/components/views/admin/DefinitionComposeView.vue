<script setup lang="ts">
import { toRefs } from 'vue'
import type { DefinitionComposeViewState } from '~/features/admin/useDefinitionCompose'

const viewProps = defineProps<{ state: DefinitionComposeViewState }>()
const { Plus, X, bytesToMib, nanoCpusToCores, addService, hasMetadata, showFlagEnvironmentVariables, definition, disabled, onUpdateModelValueResourceMemoryBytes, onUpdateModelValueResourceNanoCpus, onUpdateModelValueResourcePidsLimit, onUpdateModelValueDefinitionEnvironment, onUpdateModelValueDefinitionLabels, onUpdateModelValueDefinitionFlagEnvironmentVariables } = toRefs(viewProps.state)
</script>

<template>
  <FieldGroup>
    <Field>
      <FieldLabel>{{ $t('ui.composeYaml') }}</FieldLabel>
      <Textarea
        v-model="definition.composeYaml"
        rows="12"
        class="font-mono text-xs"
        :placeholder="$t('ui.servicesAppImage')"
        spellcheck="false"
        :disabled="disabled"
      />
      <FieldDescription> {{ $t('ui.maximum64Services1mbDangerousFieldsSuchAsPrivilegedMounting') }} </FieldDescription>
    </Field>
    <Field>
      <FieldLabel>{{ $t('ui.serviceResourceLimits') }}</FieldLabel>
      <div class="flex flex-col gap-2">
        <div
          v-for="(resource, index) in definition.serviceResources"
          :key="index"
          class="flex flex-wrap items-end gap-2 rounded-md border p-3"
        >
          <Field class="min-w-32 flex-1">
            <FieldLabel>{{ $t('ui.serviceName') }}</FieldLabel>
            <Input v-model="resource.service" :placeholder="$t('ui.app')" class="font-mono text-sm" :disabled="disabled" />
          </Field>
          <Field class="w-28">
            <FieldLabel>{{ $t('ui.memoryMib') }}</FieldLabel>
            <NullableNumberInput
              :model-value="bytesToMib(resource.memoryBytes)"
              :min="1"
              :disabled="disabled"
              @update:model-value="onUpdateModelValueResourceMemoryBytes(resource, $event)"
            />
          </Field>
          <Field class="w-28">
            <FieldLabel>{{ $t('ui.cpuCore') }}</FieldLabel>
            <NullableNumberInput
              :model-value="nanoCpusToCores(resource.nanoCpus)"
              :min="0"
              step="0.1"
              :disabled="disabled"
              @update:model-value="onUpdateModelValueResourceNanoCpus(resource, $event)"
            />
          </Field>
          <Field class="w-28">
            <FieldLabel>{{ $t('ui.maximumNumberOfProcesses') }}</FieldLabel>
            <NullableNumberInput
              :model-value="resource.pidsLimit"
              :min="1"
              :disabled="disabled"
              @update:model-value="onUpdateModelValueResourcePidsLimit(resource, $event)"
            />
          </Field>
          <Button
            v-if="!disabled"
            type="button"
            variant="ghost"
            size="icon"
            class="shrink-0"
            :aria-label="$t('ui.removeItem', { index: index + 1 })"
            @click="definition.serviceResources.splice(index, 1)"
          >
            <X class="size-4" aria-hidden="true" />
          </Button>
        </div>
        <Button
          v-if="!disabled"
          type="button"
          variant="outline"
          size="sm"
          class="w-fit"
          @click="addService(definition)"
        >
          <Plus data-icon="inline-start" /> {{ $t('ui.addService') }} </Button>
      </div>
      <FieldDescription>{{ $t('ui.everyServiceInComposeMustHaveResourceLimitsConfigured') }}</FieldDescription>
    </Field>

    <DefinitionSection :title="$t('ui.environmentMetadata')"
      accent-title
      :hint="$t('ui.environmentVariablesLabelsAndFlagInjectionTargetsMostChallengesDo')"
      :default-open="hasMetadata"
    >
      <Field>
        <FieldLabel>{{ $t('ui.environmentVariables') }}</FieldLabel>
        <KeyValueEditor
          :model-value="definition.environment"
          :key-placeholder="$t('ui.variableName')"
          :value-placeholder="$t('ui.value')"
          :add-label="$t('ui.addEnvironmentVariables')"
          :disabled="disabled"
          @update:model-value="onUpdateModelValueDefinitionEnvironment"
        />
      </Field>
      <Field>
        <FieldLabel>{{ $t('ui.label') }}</FieldLabel>
        <KeyValueEditor
          :model-value="definition.labels"
          :key-placeholder="$t('ui.tagName')"
          :value-placeholder="$t('ui.value')"
          :add-label="$t('ui.addTag')"
          :disabled="disabled"
          @update:model-value="onUpdateModelValueDefinitionLabels"
        />
      </Field>
      <Field v-if="showFlagEnvironmentVariables">
        <FieldLabel>{{ $t('ui.flagEnvironmentVariable') }}</FieldLabel>
        <KeyValueEditor
          :model-value="definition.flagEnvironmentVariables"
          :key-placeholder="$t('ui.serviceName')"
          :value-placeholder="$t('ui.environmentVariableNameSuchAsFlag')"
          :add-label="$t('ui.addInjectionTarget')"
          :disabled="disabled"
          @update:model-value="onUpdateModelValueDefinitionFlagEnvironmentVariables"
        />
        <FieldDescription>{{ $t('ui.whenInjectingFlagByTeamTheEnvironmentVariableNameCorresponding') }}</FieldDescription>
      </Field>
    </DefinitionSection>
  </FieldGroup>
</template>
