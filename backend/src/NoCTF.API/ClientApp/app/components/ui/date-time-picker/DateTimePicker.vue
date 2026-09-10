<script setup lang="ts">
import { CalendarDays } from '@lucide/vue'
import { parseDate, type DateValue } from '@internationalized/date'
import { computed, nextTick, ref, watch } from 'vue'
import { localeTag, translate } from '~/utils/i18n'
import { normalizeLocalDateTime, dateTimeWithDate, dateTimeWithTime } from './date-time'

defineOptions({ inheritAttrs: false })
const props = defineProps<{ modelValue?: string | null; id?: string; disabled?: boolean; readonly?: boolean; required?: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [value: string] }>()
const open = ref(false)
const container = ref<HTMLElement | null>(null)
const draft = ref((props.modelValue ?? '').replace('T', ' '))
const invalid = ref(false)
watch(() => props.modelValue, value => { draft.value = (value ?? '').replace('T', ' '); invalid.value = false })
const date = computed(() => {
  const normalized = normalizeLocalDateTime(draft.value)
  return normalized ? parseDate(normalized.slice(0, 10)) : undefined
})
const hour = computed(() => Number(draft.value.slice(11, 13)) || 0)
const minute = computed(() => Number(draft.value.slice(14, 16)) || 0)

function setDraft(value: string | number) {
  draft.value = String(value).replace('T', ' ')
  const normalized = normalizeLocalDateTime(draft.value)
  invalid.value = normalized === null
  if (normalized !== null) emit('update:modelValue', normalized)
}
function validateInput(event: Event) {
  const target = event.target as HTMLInputElement
  target.setCustomValidity(invalid.value ? translate('dateTime.invalid') : '')
}
watch([draft, invalid], () => nextTick(() => {
  const input = container.value?.querySelector('input')
  if (!input) return
  input.setCustomValidity(invalid.value ? translate('dateTime.invalid') : '')
  input.dispatchEvent(new Event('change', { bubbles: true }))
}))
function chooseDate(value: DateValue | undefined) {
  if (value) setDraft(dateTimeWithDate(draft.value, value.toString()))
}
function setHour(value: number | string) {
  const next = dateTimeWithTime(draft.value, Number(value), minute.value)
  if (next) setDraft(next)
}
function setMinute(value: number | string) {
  const next = dateTimeWithTime(draft.value, hour.value, Number(value))
  if (next) setDraft(next)
}
function clear() { setDraft(''); open.value = false }
</script>

<template>
  <div ref="container" data-slot="date-time-picker" :class="$attrs.class">
    <Popover v-model:open="open">
      <PopoverAnchor as-child>
      <InputGroup>
        <InputGroupInput
          v-bind="{ ...$attrs, class: undefined }"
          :id="id"
          :model-value="draft"
          :disabled="disabled"
          :readonly="readonly"
          :required="required"
          :aria-invalid="invalid || undefined"
          :aria-describedby="invalid && id ? `${id}-error` : undefined"
          :placeholder="$t('dateTime.placeholder')"
          autocomplete="off"
          @update:model-value="setDraft"
          @input="validateInput"
          @blur="validateInput"
        />
        <InputGroupAddon align="inline-end">
          <PopoverTrigger as-child>
            <InputGroupButton :disabled="disabled || readonly" size="icon-xs" :aria-label="$t('dateTime.open')"><CalendarDays /></InputGroupButton>
          </PopoverTrigger>
        </InputGroupAddon>
      </InputGroup>
      </PopoverAnchor>
      <PopoverContent align="start" class="w-auto max-w-[calc(100vw-24px)] p-2">
        <Calendar class="p-0" :model-value="date" :default-placeholder="date" :locale="localeTag()" :calendar-label="$t('dateTime.open')" layout="month-and-year" initial-focus @update:model-value="chooseDate" />
        <FieldGroup class="mt-3 grid grid-cols-2 gap-3 border-t pt-3">
          <Field><FieldLabel>{{ $t('dateTime.hour') }}</FieldLabel><NumberInput :model-value="hour" :min="0" :max="23" :disabled="!date" :aria-label="$t('dateTime.hour')" @update:model-value="setHour" /></Field>
          <Field><FieldLabel>{{ $t('dateTime.minute') }}</FieldLabel><NumberInput :model-value="minute" :min="0" :max="59" :disabled="!date" :aria-label="$t('dateTime.minute')" @update:model-value="setMinute" /></Field>
        </FieldGroup>
        <div class="mt-3 flex justify-between gap-2">
          <Button variant="ghost" size="sm" @click="clear">{{ $t('dateTime.clear') }}</Button>
          <Button size="sm" @click="open = false">{{ $t('ui.confirm') }}</Button>
        </div>
      </PopoverContent>
    </Popover>
    <p v-if="invalid" :id="id ? `${id}-error` : undefined" class="mt-1 text-sm text-destructive" role="status">{{ $t('dateTime.invalid') }}</p>
  </div>
</template>
