import { fileURLToPath } from 'node:url'
import { resolve, dirname, basename } from 'node:path'
import { readFileSync } from 'node:fs'

const root = fileURLToPath(new URL('../../app/', import.meta.url))

/** Inspect the current composition, controller and view together in source contracts. */
export function readFeatureSource(input: string | URL, _encoding = 'utf8'): string {
  const path = input instanceof URL ? fileURLToPath(input) : resolve(input)
  const source = readFileSync(path, 'utf8').replace(/\r\n/g, '\n')
  if (!path.endsWith('.vue')) return source
  const resolveImport = (specifier: string) => specifier.startsWith('~/')
    ? resolve(root, specifier.slice(2)) : resolve(dirname(path), specifier)
  const parts = [source]
  const controller = `./use${basename(path, '.vue')}`
  if (source.includes(`from '${controller}'`)) parts.push(readFileSync(resolveImport(controller + '.ts'), 'utf8').replace(/\r\n/g, '\n'))
  for (const match of source.matchAll(/import (?:Feature|View) from '([^']+)'/g)) parts.push(readFeatureSource(resolveImport(match[1]!)))
  return parts.join('\n')
}

export function sourceFile(input: string | URL) {
  return { exists: () => Bun.file(input).exists(), text: async () => readFeatureSource(input) }
}
