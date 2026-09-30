<script setup lang="ts">
import { toRefs } from 'vue'
import type { DefinitionRuntimeSectionViewState } from '~/features/admin/useDefinitionRuntimeSection'

const viewProps = defineProps<{ state: DefinitionRuntimeSectionViewState }>()
const { toggleRuntime, DefinitionRuntime, model, mode, disabled } = toRefs(viewProps.state)
</script>

<template>
  <FieldSet class="gap-4">
    <Field orientation="horizontal">
      <Switch
        id="def-has-runtime"
        :model-value="model.runtime !== null"
        :disabled="disabled"
        @update:model-value="toggleRuntime($event === true)"
      />
      <FieldLabel for="def-has-runtime" class="font-normal">{{ $t('ui.enableRuntimeEnvironment') }}</FieldLabel>
    </Field>
    <component :is="DefinitionRuntime"
      v-if="model.runtime"
      :model="model"
      :runtime="model.runtime"
      :mode="mode"
      :interaction-kind="model.interactionKind"
      :disabled="disabled"
    />
  </FieldSet>
</template>
