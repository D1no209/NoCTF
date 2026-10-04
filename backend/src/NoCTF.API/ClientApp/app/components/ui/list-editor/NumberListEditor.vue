<script setup lang="ts">
import { Plus, X } from '@lucide/vue'
import { updateNullableNumber } from '~/components/ui/list-editor/number-list'

const props = withDefaults(defineProps<{
  modelValue: Array<number | null>
  placeholder?: string
  addLabel?: string
  min?: number
  max?: number
  disabled?: boolean
}>(), {
  placeholder: '',
  addLabel: translate("common.label.addPort"),
  min: 1,
  max: 65535,
  disabled: false,
})

const emit = defineEmits<{ 'update:modelValue': [value: Array<number | null>] }>()

function update(index: number, value: number | null): void {
  emit('update:modelValue', updateNullableNumber(props.modelValue, index, value))
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
    <div v-for="(port, index) in modelValue" :key="index" class="flex flex-col gap-1">
      <div class="flex items-center gap-2">
        <NullableNumberInput
          :model-value="port"
          :min="min"
          :max="max"
          :placeholder="placeholder"
          :disabled="disabled"
          :invalid="port === null"
          @update:model-value="update(index, $event)"
        />
        <Button
          v-if="!disabled"
          type="button"
          variant="ghost"
          size="icon"
          class="shrink-0"
          :aria-label="$t('common.label.removeItem', { index: index + 1 })"
          @click="remove(index)"
        >
          <X class="size-4" aria-hidden="true" />
        </Button>
      </div>
      <p v-if="port === null" class="text-xs text-destructive">
        {{ $t('common.numberList.description.enterPortButton', { minimum: min, maximum: max }) }}
      </p>
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
      {{ translate(addLabel) }}
    </Button>
  </div>
</template>
