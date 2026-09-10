<script setup lang="ts">
import { computed } from 'vue'
import { localeTag } from '~/utils/i18n'

defineOptions({ inheritAttrs: false })
const props = withDefaults(defineProps<{
  modelValue?: number | string | null
  modelModifiers?: { number?: boolean }
  min?: number | string
  max?: number | string
  step?: number | string
  id?: string
  disabled?: boolean
  readonly?: boolean
  required?: boolean
  placeholder?: string
}>(), { step: 1 })
const emit = defineEmits<{ 'update:modelValue': [value: number | string] }>()
const value = computed(() => props.modelValue === '' || props.modelValue == null ? null : Number(props.modelValue))
const numericMin = computed(() => props.min === undefined ? undefined : Number(props.min))
const numericMax = computed(() => props.max === undefined ? undefined : Number(props.max))
const numericStep = computed(() => props.step === 'any' ? 1 : Number(props.step))
function update(next: number) { emit('update:modelValue', Number.isFinite(next) ? next : '') }
</script>

<template>
  <NumberField
    :class="$attrs.class"
    data-slot="number-input"
    :model-value="value"
    :min="numericMin"
    :max="numericMax"
    :step="numericStep"
    :step-snapping="step !== 'any'"
    :locale="localeTag()"
    :format-options="{ useGrouping: false, maximumFractionDigits: 12 }"
    :disabled="disabled"
    :readonly="readonly"
    :id="id"
    disable-wheel-change
    @update:model-value="update"
  >
    <NumberFieldContent class="relative">
      <NumberFieldDecrement :aria-label="$t('number.decrease')" />
      <NumberFieldInput v-bind="{ ...$attrs, class: undefined }" :id="id" :required="required" :placeholder="placeholder" class="px-9 text-left" />
      <NumberFieldIncrement :aria-label="$t('number.increase')" />
    </NumberFieldContent>
  </NumberField>
</template>
