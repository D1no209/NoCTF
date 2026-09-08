<script setup lang="ts">
import { Plus, X } from '@lucide/vue'

const props = withDefaults(defineProps<{
  modelValue: string[]
  placeholder?: string
  addLabel?: string
  disabled?: boolean
}>(), {
  placeholder: '',
  addLabel: translate("ui.addAnItem"),
  disabled: false,
})

const emit = defineEmits<{ 'update:modelValue': [value: string[]] }>()

function update(index: number, value: string): void {
  const next = [...props.modelValue]
  next[index] = value
  emit('update:modelValue', next)
}

function remove(index: number): void {
  emit('update:modelValue', props.modelValue.filter((_, i) => i !== index))
}

function add(): void {
  emit('update:modelValue', [...props.modelValue, ''])
}
</script>

<template>
  <div class="flex flex-col gap-2">
    <div v-for="(item, index) in modelValue" :key="index" class="flex items-center gap-2">
      <Input
        :model-value="item"
        :placeholder="placeholder"
        :disabled="disabled"
        class="font-mono text-sm"
        @update:model-value="update(index, String($event ?? ''))"
      />
      <Button
        v-if="!disabled"
        type="button"
        variant="ghost"
        size="icon"
        class="shrink-0"
        :aria-label="$t('ui.removeItem', { index: index + 1 })"
        @click="remove(index)"
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
      @click="add"
    >
      <Plus data-icon="inline-start" />
      {{ $t(addLabel) }}
    </Button>
  </div>
</template>
