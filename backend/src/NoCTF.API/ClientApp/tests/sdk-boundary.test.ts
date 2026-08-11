import { readdirSync } from 'node:fs'
import { extname, join, relative } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, test } from 'bun:test'

const appRoot = fileURLToPath(new URL('../app', import.meta.url))

function productSources(directory: string): string[] {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name)
    if (entry.isDirectory())
      return entry.name === 'api' ? [] : productSources(path)
    return ['.ts', '.vue'].includes(extname(entry.name)) ? [path] : []
  })
}

describe('generated SDK boundary', () => {
  test('keeps REST endpoint paths inside generated files', async () => {
    for (const file of productSources(appRoot)) {
      const source = await Bun.file(file).text()
      expect(source, relative(appRoot, file)).not.toMatch(/\/api\/(?:v1|internal)\//)
    }
  })

  test('only retries an already-generated request with raw fetch', async () => {
    const rawFetchFiles: string[] = []
    for (const file of productSources(appRoot)) {
      const source = await Bun.file(file).text()
      if (/\bfetch\s*\(/.test(source)) rawFetchFiles.push(relative(appRoot, file).replaceAll('\\', '/'))
      expect(source, relative(appRoot, file)).not.toContain('$fetch(')
    }

    expect(rawFetchFiles).toEqual(['plugins/api.client.ts'])
  })
})
