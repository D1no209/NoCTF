import names from './lucide-icon-names.json'
const available = new Set(names)
export function normalizeLucideIconName(value: string): string {
  return value.trim().toLowerCase().replace(/^lucide[:\-]/, '')
}
export function isLucideIconName(value: string): boolean {
  return available.has(normalizeLucideIconName(value))
}
