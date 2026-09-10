export function normalizeHex(value: unknown): string | null {
  if (typeof value !== 'string') return null
  const hex = value.trim().replace(/^#/, '')
  if (/^[\da-f]{3}$/i.test(hex)) return '#' + [...hex].map(c => c + c).join('').toUpperCase()
  return /^[\da-f]{6}$/i.test(hex) ? '#' + hex.toUpperCase() : null
}

export function hexToHsv(hex: string) {
  const [r, g, b] = [1, 3, 5].map(offset => parseInt(hex.slice(offset, offset + 2), 16) / 255) as [number, number, number]
  const max = Math.max(r, g, b), min = Math.min(r, g, b), delta = max - min
  const h = delta === 0 ? 0 : max === r ? ((g - b) / delta) % 6 : max === g ? (b - r) / delta + 2 : (r - g) / delta + 4
  return { h: (h * 60 + 360) % 360, s: max === 0 ? 0 : delta / max * 100, v: max * 100 }
}

export function hsvToHex(h: number, s: number, v: number): string {
  const saturation = Math.max(0, Math.min(100, s)) / 100, value = Math.max(0, Math.min(100, v)) / 100
  const hue = ((h % 360) + 360) % 360 / 60, chroma = value * saturation
  const x = chroma * (1 - Math.abs(hue % 2 - 1)), m = value - chroma
  const rgb = hue < 1 ? [chroma, x, 0] : hue < 2 ? [x, chroma, 0] : hue < 3 ? [0, chroma, x] : hue < 4 ? [0, x, chroma] : hue < 5 ? [x, 0, chroma] : [chroma, 0, x]
  return '#' + rgb.map(c => Math.round((c + m) * 255).toString(16).padStart(2, '0')).join('').toUpperCase()
}

export function colorInk(hex: string): string {
  const luminance = (value: string) => {
    const rgb = [1, 3, 5].map(offset => parseInt(value.slice(offset, offset + 2), 16) / 255)
      .map(c => c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4)
    return rgb[0]! * 0.2126 + rgb[1]! * 0.7152 + rgb[2]! * 0.0722
  }
  const contrast = (a: number, b: number) => (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05)
  const background = luminance(hex)
  return contrast(background, luminance('#101820')) > contrast(background, luminance('#F8FAFC')) ? '#101820' : '#F8FAFC'
}
