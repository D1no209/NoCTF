export interface FieldConstraint {
  label: string
  validity: Pick<ValidityState, 'valid' | 'valueMissing' | 'typeMismatch' | 'tooShort' | 'tooLong' | 'rangeUnderflow' | 'rangeOverflow' | 'stepMismatch' | 'patternMismatch' | 'customError' | 'badInput'>
  min?: string
  max?: string
  minLength?: number
  maxLength?: number
}

/** Browser constraint semantics, rendered with our own messages instead of native bubbles. */
export function constraintFeedback(field: FieldConstraint) {
  const values: Record<string, string | number> = { field: field.label }
  const state = field.validity
  if (state.valid) return null
  if (state.valueMissing) return { key: 'form.required' as const, values }
  if (state.tooShort) return { key: 'form.tooShort' as const, values: { ...values, count: field.minLength ?? 0 } }
  if (state.tooLong) return { key: 'form.tooLong' as const, values: { ...values, count: field.maxLength ?? 0 } }
  if (state.rangeUnderflow) return { key: 'form.minimum' as const, values: { ...values, value: field.min ?? '' } }
  if (state.rangeOverflow) return { key: 'form.maximum' as const, values: { ...values, value: field.max ?? '' } }
  return { key: 'form.invalid' as const, values }
}
