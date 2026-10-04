/** Dates arrive as Date objects from Kiota and as ISO text from SignalR/browser controls. */
export type DateValue = Date | string

export function dateTimestamp(value: DateValue | null | undefined): number {
  return value instanceof Date ? value.getTime() : Date.parse(value ?? '')
}

export function dateIso(value: DateValue | null | undefined): string | undefined {
  if (value === undefined || value === null || value === '') return undefined
  return value instanceof Date ? value.toISOString() : value
}

export function dateObject(value: DateValue): Date
export function dateObject(value: DateValue | undefined): Date | undefined
export function dateObject(value: DateValue | null): Date | null
export function dateObject(value: DateValue | null | undefined): Date | null | undefined
export function dateObject(value: DateValue | null | undefined): Date | null | undefined {
  return value === null || value === undefined || value instanceof Date ? value : new Date(value)
}
