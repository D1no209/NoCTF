export function isRuntimeUrlClickable(value: string): boolean {
  try {
    const protocol = new URL(value).protocol.toLowerCase()
    return protocol === 'http:' || protocol === 'https:'
  }
  catch {
    return false
  }
}
