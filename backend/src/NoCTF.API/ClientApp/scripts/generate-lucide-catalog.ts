import { fileURLToPath } from 'node:url'
const directory = fileURLToPath(new URL('../node_modules/@lucide/vue/dist/esm/icons/', import.meta.url))
const names = [...new Bun.Glob('*.mjs').scanSync(directory)].map(file => file.slice(0, -4)).filter(name => name !== 'index').sort()
await Bun.write(new URL('../app/lib/lucide-icon-names.json', import.meta.url), `${JSON.stringify(names, null, 2)}\n`)
await Bun.write(new URL('../../../NoCTF.Application/Competitions/Directions/lucide-icons.txt', import.meta.url), `${names.join('\n')}\n`)
console.log(`Generated ${names.length} Lucide icon suffixes for client and API.`)
