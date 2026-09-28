let cachedTheme = ''
const colors = new Map<string, string>()

/** Convert the OKLCH tokens used by the design system into Canvas-safe sRGB. */
export function oklchToHex(value: string): string | null {
  const match = value.match(/^oklch\(\s*([\d.]+)(%)?\s+([\d.]+)\s+(-?[\d.]+)(?:deg)?(?:\s*\/\s*[\d.]+%?)?\s*\)$/i)
  if (!match) return null

  const lightness = Number(match[1]) / (match[2] ? 100 : 1)
  const chroma = Number(match[3])
  const hue = Number(match[4]) * Math.PI / 180
  if (![lightness, chroma, hue].every(Number.isFinite)) return null

  const a = chroma * Math.cos(hue)
  const b = chroma * Math.sin(hue)
  const lRoot = lightness + 0.3963377774 * a + 0.2158037573 * b
  const mRoot = lightness - 0.1055613458 * a - 0.0638541728 * b
  const sRoot = lightness - 0.0894841775 * a - 1.291485548 * b
  const l = lRoot ** 3
  const m = mRoot ** 3
  const s = sRoot ** 3
  const linear = [
    4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
    -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
    -0.0041960863 * l - 0.7034186147 * m + 1.707614701 * s,
  ]
  const srgb = linear.map(channel => channel <= 0.0031308
    ? 12.92 * channel
    : 1.055 * channel ** (1 / 2.4) - 0.055)
  return '#' + srgb
    .map(channel => Math.round(Math.max(0, Math.min(1, channel)) * 255)
      .toString(16)
      .padStart(2, '0'))
    .join('')
}

/** Resolve a semantic CSS token to an sRGB color for Canvas/WebGL consumers. */
export function themeColor(token: string, element?: Element): string {
  if (typeof document === 'undefined') return '#1b2433'
  const signature = document.documentElement.className + document.documentElement.style.cssText
  if (signature !== cachedTheme) { colors.clear(); cachedTheme = signature }
  if (!element && colors.has(token)) return colors.get(token)!
  const canvas = document.createElement('canvas')
  canvas.width = canvas.height = 1
  const context = canvas.getContext('2d')!
  const raw = getComputedStyle(element ?? document.documentElement).getPropertyValue(token).trim()
  context.fillStyle = oklchToHex(raw) ?? raw
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
