/** Theme palette presets offered by the palette panel; values are design data, not runtime tokens. */
export interface ThemePalettePreset {
  color: string
  label: string
}

export const themePalettePresets: readonly ThemePalettePreset[] = [
  { color: '#0066FF', label: 'palette.blue' },
  { color: '#00B8D9', label: 'palette.cyan' },
  { color: '#8B5CF6', label: 'palette.violet' },
  { color: '#EC4899', label: 'palette.pink' },
  { color: '#F97316', label: 'palette.orange' },
  { color: '#39FF14', label: 'palette.green' },
  { color: '#EF4444', label: 'palette.red' },
  { color: '#64748B', label: 'palette.slate' },
]
