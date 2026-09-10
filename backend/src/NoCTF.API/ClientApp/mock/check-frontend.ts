/** Run after Nuxt prepare/dev/typecheck has generated both manifests. */
const production = await Bun.file(new URL('../.nuxt/components.d.ts', import.meta.url)).text()
const mock = await Bun.file(new URL('./.nuxt/components.d.ts', import.meta.url)).text()
const names = (source: string) => [...source.matchAll(/export const (\w+): typeof import\("[^"\n]*app\/components\/ui\/[^"\n]*"\)/g)].map(match => match[1]!)
const expected = names(production)
const actual = names(mock)
const missing = expected.filter(name => !actual.includes(name))
const duplicates = actual.filter((name, index) => actual.indexOf(name) !== index)
const plugins = await Bun.file(new URL('./.nuxt/types/plugins.d.ts', import.meta.url)).text()
const pluginCounts = Object.fromEntries(['api.client', 'auth-init.client', 'i18n.client', 'ui-scrollbars.client', 'theme-palette.client'].map(name => [name, plugins.split(name).length - 1]))
if (!expected.length || missing.length || duplicates.length || Object.values(pluginCounts).some(count => count !== 1)) {
  throw new Error(JSON.stringify({ missing, duplicates, pluginCounts }))
}
console.log(JSON.stringify({ uiPrimitives: actual.length, missing, duplicates, pluginCounts }, null, 2))
