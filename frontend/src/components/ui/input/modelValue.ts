export interface InputModelModifiers {
  number?: boolean
}

export function applyInputModelModifiers(
  value: string | number,
  modifiers?: InputModelModifiers,
): string | number {
  if (!modifiers?.number || typeof value !== 'string')
    return value

  const parsed = Number.parseFloat(value)
  return Number.isNaN(parsed) ? value : parsed
}
