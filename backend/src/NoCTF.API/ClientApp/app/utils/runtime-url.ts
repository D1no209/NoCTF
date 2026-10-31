export function isRuntimeUrlClickable(value: string): boolean {
  if (/\s/.test(value)) return false
  try {
    const protocol = new URL(value).protocol.toLowerCase()
    return protocol === 'http:' || protocol === 'https:'
  }
  catch {
    return false
  }
}
