<script setup lang="ts">
import { Plus, X } from '@lucide/vue'

const props = withDefaults(defineProps<{
  modelValue: number[]
  placeholder?: string
  addLabel?: string
  min?: number
  max?: number
  disabled?: boolean
}>(), {
  placeholder: '',
  addLabel: '添加端口',
  min: 1,
  max: 65535,
  disabled: false,
})

const emit = defineEmits<{ 'update:modelValue': [value: number[]] }>()

function update(index: number, value: number | null): void {
  const next = [...props.modelValue]
  if (value === null) {
    next.splice(index, 1)
  }
  else {
    next[index] = value
  }
  emit('update:modelValue', next)
}

function remove(index: number): void {
  emit('update:modelValue', props.modelValue.filter((_, i) => i !== index))
}

function add(): void {
  emit('update:modelValue', [...props.modelValue, 80])
}
</script>

<template>
  <div class="flex flex-col gap-2">
    <div v-for="(port, index) in modelValue" :key="index" class="flex items-center gap-2">
      <NullableNumberInput
        :model-value="port"
        :min="min"
        :max="max"
        :placeholder="placeholder"
        :disabled="disabled"
        @update:model-value="update(index, $event)"
      />
      <Button
        v-if="!disabled"
        type="button"
        variant="ghost"
        size="icon"
        class="shrink-0"
        @click="remove(index)"
      >
        <X class="size-4" />
      </Button>
    </div>
    <Button
      v-if="!disabled"
      type="button"
      variant="outline"
      size="sm"
      class="w-fit"
      @click="add"
    >
      <Plus data-icon="inline-start" />
      {{ addLabel }}
    </Button>
  </div>
</template>
