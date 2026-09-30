<script setup lang="ts">
import { toRefs } from 'vue'
import type { DefinitionFlagInjectionSectionViewState } from '~/features/admin/useDefinitionFlagInjectionSection'

const viewProps = defineProps<{ state: DefinitionFlagInjectionSectionViewState }>()
const { hasServices, serviceNames, toggleFlagInjection, model, disabled, onUpdateModelValueTimeoutSeconds } = toRefs(viewProps.state)
</script>

<template>
  <FieldSet class="rounded-md border p-4">
    <FieldLegend class="px-1 text-sm font-medium">{{ $t('ui.flagInjection') }}</FieldLegend>
    <Field orientation="horizontal">
      <Switch
        id="def-has-flag-injection"
        :model-value="model.flagInjection !== null"
        :disabled="disabled"
        @update:model-value="toggleFlagInjection($event === true)"
      />
      <FieldLabel for="def-has-flag-injection" class="font-normal">{{ $t('ui.injectNewFlagsIntoTheTeamEnvironmentEachRound') }}</FieldLabel>
    </Field>
    <FieldGroup v-if="model.flagInjection">
      <Field>
        <FieldLabel>{{ $t('ui.injectCommand') }}</FieldLabel>
        <Input
          v-model="model.flagInjection.command"
          :placeholder="$t('ui.shCEchoFlag')"
          class="font-mono text-sm"
          :disabled="disabled"
        />
        <FieldDescription>{{ $t('ui.mustContainThePlaceholderWhichWillBeExecutedInThe') }}</FieldDescription>
      </Field>
      <div class="grid gap-4 sm:grid-cols-2">
        <Field>
          <FieldLabel>{{ $t('ui.timeoutSeconds') }}</FieldLabel>
          <NullableNumberInput
            :model-value="model.flagInjection.timeoutSeconds"
            :min="1"
            :max="300"
            :placeholder="$t('ui.default30')"
            :disabled="disabled"
            @update:model-value="onUpdateModelValueTimeoutSeconds"
          />
        </Field>
        <Field v-if="hasServices">
          <FieldLabel>{{ $t('ui.targetServiceName') }}</FieldLabel>
          <Select v-model="model.flagInjection.serviceName" :disabled="disabled">
          <SelectTrigger><SelectValue /></SelectTrigger>
          <SelectContent><SelectGroup><SelectItem v-for="name in serviceNames" :key="name" :value="name">{{ name }}</SelectItem></SelectGroup></SelectContent>
        </Select>
          <FieldDescription>{{ $t('ui.theComposeOperatingEnvironmentMustSpecifyTheInjectionTargetService') }}</FieldDescription>
        </Field>
      </div>
    </FieldGroup>
    <FieldDescription v-else>{{ $t('ui.whenEnablingTheRuntimeEnvironmentAwdMustConfigureFlagInjection') }}</FieldDescription>
  </FieldSet>
</template>
