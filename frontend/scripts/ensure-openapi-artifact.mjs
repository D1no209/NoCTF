import { mkdir, writeFile } from 'node:fs/promises'
import { dirname, resolve } from 'node:path'

const output = resolve(process.cwd(), '../backend/artifacts/openapi/swagger.json')
const source = process.env.OPENAPI_URL ?? 'http://localhost:5000/swagger/v1/swagger.json'

const response = await fetch(source)
if (!response.ok) {
  throw new Error(`Failed to fetch OpenAPI spec from ${source}: HTTP ${response.status}`)
}

await mkdir(dirname(output), { recursive: true })
await writeFile(output, await response.text())
console.log(`Wrote ${output}`)
