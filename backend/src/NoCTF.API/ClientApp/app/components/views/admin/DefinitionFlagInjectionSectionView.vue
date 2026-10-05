<script setup lang="ts">
import { toRefs } from 'vue'
import type { DefinitionFlagInjectionSectionViewState } from '~/features/admin/useDefinitionFlagInjectionSection'

const viewProps = defineProps<{ state: DefinitionFlagInjectionSectionViewState }>()
const { hasServices, serviceNames, toggleFlagInjection, model, disabled, onUpdateModelValueTimeoutSeconds } = toRefs(viewProps.state)
</script>

<template>
  <FieldSet class="rounded-md border p-4">
    <FieldLegend class="px-1 text-sm font-medium">{{ $t('administration.label.flagInjection') }}</FieldLegend>
    <Field orientation="horizontal">
      <Switch
        id="def-has-flag-injection"
        :model-value="model.flagInjection !== null"
        :disabled="disabled"
        @update:model-value="toggleFlagInjection($event === true)"
      />
      <FieldLabel for="def-has-flag-injection" class="font-normal">{{ $t('administration.definitionFlag.description.injectNewFlagsTeam') }}</FieldLabel>
    </Field>
    <FieldGroup v-if="model.flagInjection">
      <Field>
        <FieldLabel>{{ $t('administration.label.injectCommand') }}</FieldLabel>
        <Input
          v-model="model.flagInjection.command"
          :placeholder="$t('administration.label.shCEchoFlag')"
          class="font-mono text-sm"
          :disabled="disabled"
        />
        <FieldDescription>{{ $t('administration.definitionFlag.validation.placeholderWhichFormat') }}</FieldDescription>
      </Field>
      <div class="grid gap-4 sm:grid-cols-2">
        <Field>
          <FieldLabel>{{ $t('administration.label.timeoutSeconds') }}</FieldLabel>
          <NullableNumberInput
            :model-value="model.flagInjection.timeoutSeconds"
            :min="1"
            :max="300"
            :placeholder="$t('administration.label.default')"
            :disabled="disabled"
            @update:model-value="onUpdateModelValueTimeoutSeconds"
          />
        </Field>
        <Field v-if="hasServices">
          <FieldLabel>{{ $t('administration.label.targetServiceName') }}</FieldLabel>
          <Select v-model="model.flagInjection.serviceName" :disabled="disabled">
          <SelectTrigger><SelectValue /></SelectTrigger>
          <SelectContent><SelectGroup><SelectItem v-for="name in serviceNames" :key="name" :value="name">{{ name }}</SelectItem></SelectGroup></SelectContent>
        </Select>
          <FieldDescription>{{ $t('administration.definitionFlag.validation.composeOperatingFormat') }}</FieldDescription>
        </Field>
      </div>
    </FieldGroup>
    <FieldDescription v-else>{{ $t('administration.definitionFlag.validation.enablingRuntimeFormat') }}</FieldDescription>
  </FieldSet>
</template>
