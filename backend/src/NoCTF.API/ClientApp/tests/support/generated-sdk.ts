import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'

/** Read Kiota's generated operation metadata across its request-builder tree. */
export async function generatedSdkSource(): Promise<string> {
  const root = fileURLToPath(new URL('../../app/api', import.meta.url))
  function read(directory: string): string[] {
    return readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
      const path = join(directory, entry.name)
      return entry.isDirectory() ? read(path) : entry.name.endsWith('.ts') ? [readFileSync(path, 'utf8')] : []
    })
  }
  return read(root).join('\n')
}
