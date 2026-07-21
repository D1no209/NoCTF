// Shared numeric coercion helpers for feature modules.
// Forms hold numeric fields as strings (command-style inputs emit strings);
// payload builders convert at the API boundary.

export function numberOrDefault(value: number | string | undefined | null, fallback: number): number {
  if (value === undefined || value === null || value === '')
    return fallback
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : fallback
}

export function optionalNumber(value: number | string | undefined | null): number | undefined {
  if (value === undefined || value === null || value === '')
    return undefined
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : undefined
}

// Preserves V1's v-model.number settings contract: the declared payload type
// stays numeric while a cleared field keeps '' at runtime.
export function numberField(value: string): number {
  return (value.trim() === '' ? '' : Number(value)) as number
}

export function optionalPositiveNumber(value: string | undefined | null): number | undefined {
  const trimmed = value?.trim()
  return trimmed ? Number(trimmed) : undefined
}

// Hydration fallback chain: first candidate that is not undefined/null/'',
// returned as a string.
export function firstNumberString(...candidates: Array<number | string | undefined | null>): string {
  for (const candidate of candidates) {
    if (candidate === undefined || candidate === null || candidate === '')
      continue
    return String(candidate)
  }
  return ''
}
