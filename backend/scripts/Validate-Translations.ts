import { readFileSync, readdirSync, existsSync } from 'node:fs'
import { resolve, join } from 'node:path'

export const locales = ['en', 'zh-CN'] as const
export type Catalog = Record<string, string>
export const semanticKey = /^[a-z][a-zA-Z]*(?:\.[a-zA-Z][a-zA-Z]*)+$/

/** Catalogs are flat JSON objects. Parse entries before JSON.parse can discard duplicates. */
export function parseCatalog(source: string, name = 'catalog'): Catalog {
  const result: Catalog = Object.create(null)
  const entry = /\s*("(?:[^"\\]|\\.)*")\s*:\s*("(?:[^"\\]|\\.)*")\s*/y
  const text = source.trim()
  if (!text.startsWith('{') || !text.endsWith('}')) throw new Error(`${name}: expected a flat JSON object`)
  let position = 1
  while (text.slice(position, -1).trim()) {
    entry.lastIndex = position
    const match = entry.exec(text)
    if (!match) throw new Error(`${name}: invalid catalog entry at ${position}`)
    const key: string = JSON.parse(match[1]!)
    if (!semanticKey.test(key) || key.startsWith('ui.')) throw new Error(`${name}: non-semantic key ${key}`)
    if (Object.hasOwn(result, key)) throw new Error(`${name}: duplicate key ${key}`)
    result[key] = JSON.parse(match[2]!)
    position = entry.lastIndex
    if (text[position] === ',') {
      position++
      if (!text.slice(position, -1).trim()) throw new Error(`${name}: trailing comma`)
    }
    else if (position !== text.length - 1) throw new Error(`${name}: invalid separator`)
  }
  // Also reject invalid escaping, raw newlines and trailing content.
  JSON.parse(text)
  return result
}

export function readCatalog(path: string): Catalog {
  return parseCatalog(readFileSync(path, 'utf8'), path)
}

export function parameters(text: string): string[] {
  return [...new Set([...text.matchAll(/\{(\w+)\}/g)].map(match => match[1]!))].sort()
}

export function validateCatalog(source: Catalog, translation: Catalog, name: string): void {
  for (const [key, value] of Object.entries(source)) {
    if (!value.trim()) throw new Error(`${name}: empty English source ${key}`)
  }
  for (const [key, value] of Object.entries(translation)) {
    if (!Object.hasOwn(source, key)) throw new Error(`${name}: unknown key ${key}`)
    if (value.trim() && JSON.stringify(parameters(value)) !== JSON.stringify(parameters(source[key]!)))
      throw new Error(`${name}: interpolation parameters differ for ${key}`)
  }
}

export function validateTranslations(root: string, backendOnly = false): void {
  const directories = [join(root, 'backend/src/NoCTF.API/Localization/Catalogs')]
  if (!backendOnly) directories.push(join(root, 'backend/src/NoCTF.API/ClientApp/app/locales/catalogs'))
  for (const directory of directories) {
    const owned = new Set<string>()
    for (const file of readdirSync(join(directory, 'en')).filter(file => file.endsWith('.json'))) {
      const source = readCatalog(join(directory, 'en', file))
      for (const key of Object.keys(source)) {
        if (owned.has(key)) throw new Error(`${directory}: key owned by multiple domains: ${key}`)
        owned.add(key)
      }
      for (const locale of locales) {
        const path = join(directory, locale, file)
        validateCatalog(source, existsSync(path) ? readCatalog(path) : {}, path)
      }
    }
  }
}

if (import.meta.main) {
  validateTranslations(resolve(process.argv[2] ?? '.'), process.argv.includes('--backend-only'))
  console.log('Translation catalogs validated')
}
