import { readdirSync } from 'node:fs'

/** Run after Nuxt prepare/dev/typecheck has generated both manifests. */
const production = await Bun.file(new URL('../.nuxt/components.d.ts', import.meta.url)).text()
const mock = await Bun.file(new URL('./.nuxt/components.d.ts', import.meta.url)).text()
const names = (source: string) => [...source.matchAll(/export const (\w+): typeof import\("[^"\n]*app\/components\/ui\/[^"\n]*"\)/g)].map(match => match[1]!)
const expected = names(production)
const actual = names(mock)
const missing = expected.filter(name => !actual.includes(name))
const duplicates = actual.filter((name, index) => actual.indexOf(name) !== index)
const plugins = await Bun.file(new URL('./.nuxt/types/plugins.d.ts', import.meta.url)).text()
// 期望插件从 app/plugins 目录枚举,新增插件无需再手工维护清单。
const pluginNames = readdirSync(new URL('../app/plugins', import.meta.url))
  .filter(name => name.endsWith('.ts'))
  .map(name => name.slice(0, -'.ts'.length))
const pluginCounts = Object.fromEntries(pluginNames.map(name => [name, plugins.split(name).length - 1]))
if (!expected.length || missing.length || duplicates.length || Object.values(pluginCounts).some(count => count !== 1)) {
  throw new Error(JSON.stringify({ missing, duplicates, pluginCounts }))
}
console.log(JSON.stringify({ uiPrimitives: actual.length, missing, duplicates, pluginCounts }, null, 2))
