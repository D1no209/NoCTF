<script setup lang="ts">
import { Eye, EyeOff } from '@lucide/vue'

defineOptions({ inheritAttrs: false })

const props = defineProps<{
  modelValue?: string
}>()

const emit = defineEmits<{
  (event: 'update:modelValue', value: string): void
}>()

const visible = ref(false)

function updateValue(value: string | number) {
  emit('update:modelValue', String(value))
}
</script>

<template>
  <InputGroup>
    <InputGroupInput
      v-bind="$attrs"
      :model-value="props.modelValue"
      :type="visible ? 'text' : 'password'"
      @update:model-value="updateValue"
    />
    <InputGroupAddon align="inline-end">
      <InputGroupButton
        size="icon-xs"
        :aria-label="$t(visible ? 'ui.hidePassword' : 'ui.showPassword')"
        :aria-pressed="visible"
        @click="visible = !visible"
      >
        <EyeOff v-if="visible" aria-hidden="true" />
        <Eye v-else aria-hidden="true" />
      </InputGroupButton>
    </InputGroupAddon>
  </InputGroup>
</template>
