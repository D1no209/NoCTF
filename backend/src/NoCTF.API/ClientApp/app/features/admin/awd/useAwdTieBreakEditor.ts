import { computed } from 'vue'
import { ArrowUp, ArrowDown, Plus, Trash2, RotateCcw } from '@lucide/vue'
import { awdTieBreakFields, defaultAwdTieBreaks, validAwdTieBreaks, type AwdTieBreak } from '../../../utils/awd-tie-breaks'
import { translate } from '../../../utils/i18n'

export interface AwdTieBreakEditorProps { modelValue: AwdTieBreak[]; disabled?: boolean }
export function useAwdTieBreakEditor(props: AwdTieBreakEditorProps, emit: (event: 'update:modelValue', value: AwdTieBreak[]) => void) {
  const rows = computed(() => props.modelValue.map((row, index) => ({ ...row, index,
    choices: awdTieBreakFields.filter(field => field === row.field || !props.modelValue.some(rule => rule.field === field))
      .map(field => ({ value: field, label: translate(`administration.awd.tieBreak.field.${field}`) })) })))
  const valid = computed(() => validAwdTieBreaks(props.modelValue))
  const canAdd = computed(() => !props.disabled && props.modelValue.length < awdTieBreakFields.length)
  function updateField(index: number, value: unknown) {
    if (props.disabled || !props.modelValue[index] || !awdTieBreakFields.some(field => field === value)
      || props.modelValue.some((rule, i) => i !== index && rule.field === value)) return
    emit('update:modelValue', props.modelValue.map((row, i) => i === index ? { ...row, field: value as AwdTieBreak['field'] } : { ...row }))
  }
  function updateDirection(index: number, value: unknown) {
    if (props.disabled || !props.modelValue[index] || (value !== 'Ascending' && value !== 'Descending')) return
    emit('update:modelValue', props.modelValue.map((row, i) => i === index ? { ...row, direction: value } : { ...row }))
  }
  function move(index: number, delta: number) {
    const target = index + delta
    if (props.disabled || !props.modelValue[index] || !props.modelValue[target]) return
    const next = props.modelValue.map(row => ({ ...row })); [next[index], next[target]] = [next[target]!, next[index]!]
    emit('update:modelValue', next)
  }
  function remove(index: number) {
    if (props.disabled || props.modelValue.length <= 1) return
    emit('update:modelValue', props.modelValue.filter((_, i) => i !== index).map(row => ({ ...row })))
  }
  function add() {
    if (!canAdd.value) return
    const next = defaultAwdTieBreaks().find(rule => !props.modelValue.some(row => row.field === rule.field))
    if (next) emit('update:modelValue', [...props.modelValue.map(row => ({ ...row })), next])
  }
  function reset() { if (!props.disabled) emit('update:modelValue', defaultAwdTieBreaks()) }
  return { rows, valid, canAdd, disabled: computed(() => props.disabled), updateField, updateDirection, move, remove, add, reset, ArrowUp, ArrowDown, Plus, Trash2, RotateCcw }
}
export type AwdTieBreakEditorViewState = import('vue').ShallowUnwrapRef<ReturnType<typeof useAwdTieBreakEditor>>
