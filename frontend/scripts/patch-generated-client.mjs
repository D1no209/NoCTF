import { readFile, writeFile } from 'node:fs/promises'
import { fileURLToPath } from 'node:url'
import { dirname, resolve } from 'node:path'

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const clientFile = resolve(root, 'src/api/generated/client.gen.ts')
const typesFile = resolve(root, 'src/api/generated/types.gen.ts')

let clientSource = await readFile(clientFile, 'utf8')
if (!clientSource.includes("import { createClientConfig } from '../client-config';")) {
  clientSource = clientSource.replace(
    "import { type ClientOptions, type Config, createClient, createConfig } from './client';",
    "import { type ClientOptions, type Config, createClient, createConfig } from './client';\nimport { createClientConfig } from '../client-config';",
  )
}

clientSource = clientSource.replace(
  /export const client = createClient\(createConfig<ClientOptions2>\(\{ baseUrl: '[^']*' \}\)\);/,
  'export const client = createClient(createConfig<ClientOptions2>(createClientConfig()));',
)
await writeFile(clientFile, clientSource)

let typesSource = await readFile(typesFile, 'utf8')
typesSource = typesSource.replace(/baseUrl: '[^']*' \| \(string & \{\}\);/, 'baseUrl: string;')
await writeFile(typesFile, typesSource)
