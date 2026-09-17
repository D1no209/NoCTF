import hljs from 'highlight.js/lib/core'
import bash from 'highlight.js/lib/languages/bash'
import c from 'highlight.js/lib/languages/c'
import cpp from 'highlight.js/lib/languages/cpp'
import csharp from 'highlight.js/lib/languages/csharp'
import css from 'highlight.js/lib/languages/css'
import dockerfile from 'highlight.js/lib/languages/dockerfile'
import go from 'highlight.js/lib/languages/go'
import java from 'highlight.js/lib/languages/java'
import javascript from 'highlight.js/lib/languages/javascript'
import json from 'highlight.js/lib/languages/json'
import markdown from 'highlight.js/lib/languages/markdown'
import nginx from 'highlight.js/lib/languages/nginx'
import php from 'highlight.js/lib/languages/php'
import powershell from 'highlight.js/lib/languages/powershell'
import python from 'highlight.js/lib/languages/python'
import rust from 'highlight.js/lib/languages/rust'
import sql from 'highlight.js/lib/languages/sql'
import typescript from 'highlight.js/lib/languages/typescript'
import xml from 'highlight.js/lib/languages/xml'
import x86asm from 'highlight.js/lib/languages/x86asm'
import yaml from 'highlight.js/lib/languages/yaml'
export { normalizeMarkdownCodeLanguage } from './markdown-language'

const languages = {
  bash,
  c,
  cpp,
  csharp,
  css,
  dockerfile,
  go,
  java,
  javascript,
  json,
  markdown,
  nginx,
  php,
  powershell,
  python,
  rust,
  sql,
  typescript,
  xml,
  x86asm,
  yaml,
}

for (const [name, definition] of Object.entries(languages))
  hljs.registerLanguage(name, definition)

hljs.registerAliases(['sh', 'shell', 'zsh'], { languageName: 'bash' })
hljs.registerAliases(['c++', 'cc', 'h', 'hpp'], { languageName: 'cpp' })
hljs.registerAliases(['cs', 'dotnet'], { languageName: 'csharp' })
hljs.registerAliases(['html', 'xhtml', 'svg'], { languageName: 'xml' })
hljs.registerAliases(['js', 'jsx'], { languageName: 'javascript' })
hljs.registerAliases(['md'], { languageName: 'markdown' })
hljs.registerAliases(['ps1'], { languageName: 'powershell' })
hljs.registerAliases(['py'], { languageName: 'python' })
hljs.registerAliases(['rs'], { languageName: 'rust' })
hljs.registerAliases(['ts', 'tsx'], { languageName: 'typescript' })
hljs.registerAliases(['yml'], { languageName: 'yaml' })

export function highlightMarkdownCode(code: string, language: string): string | null {
  if (!language || !hljs.getLanguage(language)) return null
  return hljs.highlight(code, { language, ignoreIllegals: true }).value
}
