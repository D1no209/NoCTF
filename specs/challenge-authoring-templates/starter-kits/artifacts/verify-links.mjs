import { readFile, readdir, access } from 'node:fs/promises'
import path from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'
import { createMarkdownRenderer } from 'E:/SourceCode/NoCTF/docs/node_modules/vitepress/dist/node/index.js'

const root = 'E:/SourceCode/NoCTF'
const md = await createMarkdownRenderer(path.join(root, 'docs'))
const files = []
async function collect(directory) {
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    if (['artifacts', 'dist', 'examples', 'node_modules'].includes(entry.name)) continue
    const target = path.join(directory, entry.name)
    if (entry.isDirectory()) await collect(target)
    else if (entry.name.endsWith('.md')) files.push(target)
  }
}
await collect(path.join(root, 'specs/challenge-authoring-templates'))
files.push(...[
  'specs/challenge-authoring.md', 'specs/challenge-authoring-awd-awdp.md',
  'specs/README.md', 'specs/game-modes/koh.md',
  'docs/challenge-bank/authoring.md', 'docs/challenge-bank/index.md',
  'docs/challenge-bank/templates.md', 'docs/challenge-bank/runtime-checkers.md'
].map(file => path.join(root, file)))
const failures = []
let checked = 0
async function inspect(tokens, file) {
  for (const token of tokens) {
    const href = token.type === 'link_open' ? token.attrGet('href') : token.type === 'image' ? token.attrGet('src') : null
    if (href && !href.startsWith('#')) {
      const url = new URL(href, pathToFileURL(file))
      if (url.protocol === 'file:') {
        url.hash = ''
        url.search = ''
        let target = fileURLToPath(url)
        if (href.startsWith('/') && file.startsWith(path.join(root, 'docs'))) target = path.join(root, 'docs', href)
        const options = path.extname(target) ? [target] : [target + '.md', path.join(target, 'index.md')]
        let found = false
        for (const candidate of options) {
          try { await access(candidate); found = true; break } catch {}
        }
        if (!found) failures.push(path.relative(root, file) + ': ' + href)
        checked++
      }
    }
    if (token.children) await inspect(token.children, file)
  }
}
for (const file of files) await inspect(md.parse(await readFile(file, 'utf8'), {}), file)
if (failures.length) throw new Error(failures.join('\n'))
console.log(`${files.length} Markdown files and ${checked} local links passed`)
