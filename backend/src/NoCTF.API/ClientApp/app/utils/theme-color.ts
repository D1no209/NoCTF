let cachedTheme = ''
const colors = new Map<string, string>()

/** Resolve a semantic CSS token to an sRGB color for Canvas/WebGL consumers. */
export function themeColor(token: string, element?: Element): string {
  if (typeof document === 'undefined') return '#1b2433'
  const signature = document.documentElement.className + document.documentElement.style.cssText
  if (signature !== cachedTheme) { colors.clear(); cachedTheme = signature }
  if (!element && colors.has(token)) return colors.get(token)!
  const canvas = document.createElement('canvas')
  canvas.width = canvas.height = 1
  const context = canvas.getContext('2d')!
  context.fillStyle = getComputedStyle(element ?? document.documentElement).getPropertyValue(token).trim()
  context.fillRect(0, 0, 1, 1)
  const [r, g, b] = context.getImageData(0, 0, 1, 1).data
  const color = `#${[r!, g!, b!].map(value => value.toString(16).padStart(2, '0')).join('')}`
  if (!element) colors.set(token, color)
  return color
}

export function themeColorAlpha(token: string, alpha: number, element?: Element): string {
  const color = themeColor(token, element)
  const rgb = [1, 3, 5].map(offset => Number.parseInt(color.slice(offset, offset + 2), 16))
  return `rgba(${rgb.join(',')},${alpha})`
}
