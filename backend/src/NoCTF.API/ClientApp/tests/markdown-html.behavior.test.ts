import { afterAll, expect, test } from 'bun:test'
import { Window } from 'happy-dom'
import { markdownHtmlBlockRanges, renderMarkdown, renderMarkdownAsync, renderPublishedMarkdown } from '../app/lib/markdown'
import { renderMarkdownPreview } from '../app/lib/markdown-preview-worker.client'

const browser = new Window()
afterAll(() => browser.happyDOM.abort())

const renderers = [renderMarkdown, renderMarkdownAsync, renderPublishedMarkdown, renderMarkdownPreview]

test('author alignment and nested formatting survive every Markdown rendering entry point', async () => {
  const source = '<div style="text-align: right">出题人：<strong>PaperPlane</strong></div>'
  for (const render of renderers) {
    const template = browser.document.createElement('template')
    template.innerHTML = await render(source)
    const author = template.content.querySelector('div')
    expect(author?.style.textAlign).toBe('right')
    expect(author?.textContent).toBe('出题人：PaperPlane')
    expect(author?.querySelector('strong')?.textContent).toBe('PaperPlane')
  }
})

test('asset-restricted documents keep author alignment without restoring remote media', async () => {
  const html = await renderMarkdownAsync('<div style="text-align:right">出题人：PaperPlane</div>\n\n<img src="https://example.com/track"><video src="https://example.com/track"></video>', {})
  const template = browser.document.createElement('template')
  template.innerHTML = html
  expect(template.content.querySelector('div')?.style.textAlign).toBe('right')
  expect(template.content.querySelector('img,video,source,audio')).toBeNull()
})

const unsafeFragments = [
  '<SCRIPT>window.attack = true</SCRIPT>',
  '<iframe srcdoc="<script>window.attack=true</script>"></iframe>',
  '<object data="data:text/html,bad"><embed src="https://example.com"></object>',
  '<svg><foreignObject><p onclick="bad()">bad</p></foreignObject></svg>',
  '<math><mtext><table><mglyph><style><!--</style><img title="--><img src=x onerror=bad()>">',
  '<form action="/api/delete"><input autofocus onfocus="bad()"><button formaction="/api/delete">bad</button></form>',
  '<base href="https://example.com"><meta http-equiv="refresh" content="0;url=https://example.com"><link rel="stylesheet" href="https://example.com">',
  '<a href="java&#x09;script:bad()" onclick="bad()">link</a><img src="data:image/svg+xml,bad" onerror="bad()">',
  '<div id="app" name="location" class="page-wallpaper" contenteditable="true" style="text-align:right;position:fixed;z-index:999999;background:url(https://example.com);width:expression(bad());--custom:url(https://example.com)">safe</div>',
]

for (const [index, attack] of unsafeFragments.entries()) {
  test(`forbidden HTML cannot survive publication or preview (${index + 1})`, async () => {
    for (const render of renderers) {
      const template = browser.document.createElement('template')
      template.innerHTML = await render(`<div style="text-align:right">safe author</div>\n\n${attack}`)
      expect(template.content.querySelector('script,style,iframe,object,embed,svg,math,form,input,button,base,meta,link,template')).toBeNull()
      for (const element of template.content.querySelectorAll('*')) {
        for (const attribute of element.attributes) {
          expect(attribute.name).not.toMatch(/^(?:on|id$|name$|srcdoc$|contenteditable$|formaction$)/i)
          if (['href', 'src', 'poster'].includes(attribute.name))
            expect(attribute.value).not.toMatch(/^(?:javascript|vbscript|data|file):/i)
          if (attribute.name === 'style')
            expect(attribute.value).not.toMatch(/position|z-index|url\s*\(|expression|--custom/i)
        }
      }
      expect(template.content.textContent).toContain('safe author')
    }
  })
}

test('HTML block ranges come from Markdown parsing and exclude literal HTML inside code fences', () => {
  const source = 'Intro\n\n<div style="text-align:right">\nAuthor\n</div>\n\n```html\n<div>literal</div>\n```'
  expect(markdownHtmlBlockRanges(source)).toEqual([{ fromLine: 3, toLine: 5 }])
  expect(markdownHtmlBlockRanges('`<div>literal</div>`')).toEqual([])
})
