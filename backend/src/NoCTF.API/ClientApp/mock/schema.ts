import { readFileSync } from 'node:fs'

export type Data = Record<string, any>
export const spec = JSON.parse(readFileSync(new URL('../../../../artifacts/openapi/v1.json', import.meta.url), 'utf8')) as Data
export const now = () => new Date().toISOString()
export const date = (hours: number) => new Date(Date.now() + hours * 3_600_000).toISOString()
export const id = (group: number, index = 1) => `${group.toString(16).padStart(8, '0')}-0000-4000-8000-${index.toString(16).padStart(12, '0')}`

export function resolve(schema: Data = {}): Data {
  return schema.$ref ? spec.components.schemas[schema.$ref.split('/').at(-1)] : schema
}

/** Empty, schema-shaped responses for secondary read-only screens, never fake writes. */
export function sample(schema: Data = {}, depth = 0): any {
  if (depth > 12 || schema.nullable) return null
  schema = resolve(schema)
  if (schema.nullable) return null
  if (schema.enum) return schema.enum[0]
  if (schema.allOf) return Object.assign({}, ...schema.allOf.map((part: Data) => sample(part, depth + 1)))
  if (schema.oneOf || schema.anyOf) return sample((schema.oneOf ?? schema.anyOf)[0], depth + 1)
  if (schema.type === 'array') return []
  if (schema.properties) return Object.fromEntries(Object.entries(schema.properties).map(([key, value]) => [key, sample(value as Data, depth + 1)]))
  if (schema.type === 'boolean') return false
  if (schema.type === 'integer' || schema.type === 'number') return schema.minimum ?? 0
  if (schema.format === 'guid' || schema.format === 'uuid') return id(0)
  if (schema.format === 'date-time') return now()
  if (schema.type === 'string') return ''
  return {}
}

export function model(suffix: string, values: Data = {}): Data {
  const names = Object.keys(spec.components.schemas).filter(name => name.startsWith('NoCTF') && name.endsWith(suffix))
  if (names.length !== 1) throw new Error(`Ambiguous mock schema: ${suffix}: ${names.join(', ')}`)
  return { ...sample(spec.components.schemas[names[0]]), ...values }
}

export const operations = Object.entries(spec.paths).flatMap(([path, methods]) =>
  Object.entries(methods as Data).filter(([method]) => /^(get|post|put|delete|patch)$/.test(method)).map(([method, operation]) => ({
    path, method: method.toUpperCase(), operation: operation as Data,
    keys: [...path.matchAll(/\{(\w+)\}/g)].map(match => match[1]!),
    pattern: new RegExp(`^${path.replace(/[.*+?^$()|[\]\\]/g, '\\$&').replace(/\{\w+\}/g, '([^/]+)')}$`),
  })),
).sort((a, b) => a.keys.length - b.keys.length)

export function matchOperation(path: string, method: string) {
  for (const item of operations) {
    const match = method === item.method && path.match(item.pattern)
    if (match) return { ...item, params: Object.fromEntries(item.keys.map((key, i) => [key, decodeURIComponent(match[i + 1]!)])) }
  }
  return null
}

export function responseSchema(operation: Data, status = '200') {
  return operation.responses?.[status]?.content?.['application/json']?.schema
}
