import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'
import { renderMarkdown, renderMarkdownAsync, renderPublishedMarkdown } from '../app/lib/markdown'
import { highlightMarkdownCode, normalizeMarkdownCodeLanguage } from '../app/lib/markdown-highlighter'
import { readableHintContent } from '../app/lib/challenge-hints'

describe('safe challenge Markdown', () => {
  test('supports the three common HTML centering forms', () => {
    expect(renderMarkdown('<center>居中标题</center>')).toContain('<div style="text-align:center">居中标题</div>')
    expect(renderMarkdown('<div align="center">居中内容</div>')).toContain('<div style="text-align:center">居中内容</div>')
    expect(renderMarkdown('<p style="text-align: center">居中段落</p>')).toContain('<p style="text-align:center">居中段落</p>')
    expect(renderMarkdown('<div align="CENTER">\n\n**居中 Markdown**\n\n</div>'))
      .toContain('<strong>居中 Markdown</strong>')
  })

  test('keeps common semantic HTML, safe formatting, tables and disclosures', () => {
    const html = renderMarkdown('<section><h2>说明</h2><p><b>粗体</b><i>斜体</i><u>下划线</u><del>删除</del><mark>重点</mark><sub>下标</sub></p><details open><summary>展开</summary><p>详情</p></details><table><tr><td colspan="2">跨列</td></tr></table></section>')
    for (const value of ['<section>', '<b>粗体</b>', '<i>斜体</i>', '<u>下划线</u>', '<del>删除</del>', '<mark>重点</mark>', '<sub>下标</sub>', '<details open>', '<summary>展开</summary>', 'colspan="2"'])
      expect(html).toContain(value)
  })

  test('keeps safe styles but removes page overlays, CSS URLs and expressions', () => {
    const html = renderMarkdown('<div align="center" style="color:red;font-size:20px;font-weight:bold;position:fixed;inset:0;z-index:9999;background-image:url(https://example.com/track);width:expression(alert(1))" onclick="alert(1)">排版</div>')
    expect(html).toContain('text-align:center')
    expect(html).toContain('color:red')
    expect(html).toContain('font-size:20px')
    expect(html).not.toMatch(/position|inset|z-index|background-image|expression|onclick|example\.com/)
  })

  test('removes scripts, embedded documents, forms, metadata and DOM-clobbering attributes', () => {
    const html = renderMarkdown("<div id=\"app\" name=\"location\"><script>alert(1)</script><style>body{display:none}</style><iframe src=\"https://example.com\"></iframe><object data=\"bad\"></object><form action=\"/api/delete\"><input name=\"password\"><ActionButton>提交</button></form><base href=\"https://example.com\"><meta http-equiv=\"refresh\" content=\"0;url=https://example.com\"><svg onload=\"alert(1)\"></svg><math></math>正文</div>")
    expect(html).not.toMatch(/<(script|style|iframe|object|form|input|button|base|meta|svg|math)\b/)
    expect(html).not.toMatch(/\s(?:id|name|onload|action)=/)
    expect(html).toContain("正文")
  })

  test('sanitizes raw HTML links and images, including encoded protocols and events', () => {
    const html = renderMarkdown('<a href="java&#10;script:alert(1)" onclick="bad()">危险</a><a href="https://example.com" target="_blank">安全</a><img src="data:image/svg+xml;base64,PHN2Zz4=" onerror="bad()"><img src="https://example.com/p.png" onerror="bad()">')
    expect(html).not.toMatch(/href="java|onerror=|onclick=|src="data:/i)
    expect(html).toContain('rel="noopener noreferrer"')
    expect(html).toContain('src="https://example.com/p.png"')
    expect(html).toContain('referrerpolicy="no-referrer"')
  })

  test('allows opt-in playback without automatic media loading or unsafe poster URLs', () => {
    const html = renderMarkdown('<video src="https://example.com/v.mp4" poster="javascript:alert(1)" autoplay onplay="bad()"></video><audio src="https://example.com/a.mp3" autoplay></audio>')
    expect(html).toContain('<video')
    expect(html).toContain('<audio')
    expect(html.match(/preload="none"/g)).toHaveLength(2)
    expect(html.match(/controls/g)).toHaveLength(2)
    expect(html).not.toMatch(/autoplay|onplay|javascript|poster=/)
  })

  test('renders the author line, explicit breaks and bold hint text', () => {
    const html = renderMarkdown('**出题人：PaperPlane**<br>\n难度：⭐⭐⭐<br>\n如遇环境问题请联系出题人')
    expect(html).toContain('<strong>出题人：PaperPlane</strong><br />')
    expect(html).not.toContain('&lt;br&gt;')
    expect(renderMarkdown('**动调！！！**')).toContain('<strong>动调！！！</strong>')
    expect(renderMarkdown('a<br/>b<BR />c')).toContain('a<br />b<br />c')
  })

  test('supports headings, lists, quotes, inline code, fenced code, tables and links', async () => {
    const html = await renderMarkdownAsync('# 标题\n\n- 第一项\n- 第二项\n\n1. 步骤\n\n> 引用\n\n`flag{...}`\n\n```python\nprint("hello")\n```\n\n| 字段 | 值 |\n| --- | --- |\n| A | B |\n\n[文档](https://example.com/docs)')
    for (const tag of ['<h1>', '<ul>', '<ol>', '<blockquote>', '<code>', '<pre tabindex="0" data-language="python">', '<table tabindex="0">', '<th>'])
      expect(html).toContain(tag)
    expect(html).toContain('class="hljs language-python"')
    expect(html).toContain('class="hljs-built_in"')
    expect(html).toContain('href="https://example.com/docs" rel="noopener noreferrer" target="_blank"')
  })

  test('highlights registered fence languages and safely falls back for unknown or malformed info strings', async () => {
    expect(normalizeMarkdownCodeLanguage(' TypeScript extra')).toBe('typescript')
    expect(normalizeMarkdownCodeLanguage('\"><img/src=x>')).toBe('')
    expect(highlightMarkdownCode('const answer: number = 42', 'typescript')).toContain('hljs-keyword')
    expect(highlightMarkdownCode('<script>alert(1)</script>', 'unknown')).toBeNull()

    const fallback = await renderMarkdownAsync('```unknown\n<script>alert(1)</script>\n```')
    expect(fallback).toContain('<pre tabindex="0" data-language="unknown"><code class="hljs language-unknown">')
    expect(fallback).toContain('&lt;script&gt;alert(1)&lt;/script&gt;')
    expect(fallback).not.toContain('<script>')
  })

  test('filters unsafe HTML and attributes while keeping code and escaped tags literal', async () => {
    const html = renderMarkdown('<script>alert(1)</script>\n\n<img src=x onerror=alert(1)>\n\n<br onclick="alert(1)">\n\n<svg/onload=alert(1)>')
    expect(html).not.toMatch(/<(script|svg)\b/i)
    expect(html).not.toMatch(/<[^>]*\son(?:error|click|load)\s*=/i)
    expect(html).not.toContain('<script>')
    expect(renderMarkdown('`<br>`')).toContain('<code>&lt;br&gt;</code>')
    expect(renderMarkdown('\\<br>')).toContain('&lt;br&gt;')
    const highlightedHtml = await renderMarkdownAsync('```html\n<br>\n<script>alert(1)</script>\n```')
    expect(highlightedHtml).toContain('class="hljs-tag"')
    expect(highlightedHtml).not.toContain('<script>')
  })

  for (const url of ['javascript:alert(1)', 'JaVaScRiPt:alert(1)', 'jav&#x61;script:alert(1)', 'vbscript:msgbox(1)', 'file:///etc/passwd', 'data:text/html;base64,PHNjcmlwdD4=']) {
    test(`rejects unsafe link protocol: ${url}`, () => {
      expect(renderMarkdown(`[click](${url})`)).not.toContain('<a ')
    })
  }

  test('images use safe attributes and reject executable SVG data URLs', () => {
    const html = renderMarkdown('![示意图](https://example.com/image.png "title")')
    expect(html).toContain('<img src="https://example.com/image.png"')
    expect(html).toContain('loading="lazy"')
    expect(html).toContain('referrerpolicy="no-referrer"')
    expect(renderMarkdown('![bad](data:image/svg+xml;base64,PHN2Zz4=)')).not.toContain('<img ')
  })

  test('caches immutable published documents by source hash', () => {
    const first = renderPublishedMarkdown('# Cached document')
    const second = renderPublishedMarkdown('# Cached document')
    expect(second).toBe(first)
  })

  test('locked hints never reach the Markdown renderer', async () => {
    expect(readableHintContent({ isUnlocked: false, content: '![private](https://example.com/secret)' })).toBeNull()
    const hints = await sourceFile(new URL('../app/features/challenges/ChallengeHints.vue', import.meta.url)).text()
    const detail = await sourceFile(new URL('../app/features/challenges/CompetitionChallengeDetail.vue', import.meta.url)).text()
    expect(hints).toContain('<MarkdownQuote v-if="readableHintContent(hint) !== null"')
    expect(detail).toContain('<MarkdownContent :source="challenge.description"')
    expect(hints).not.toContain("$t('ui.free')")
    expect(hints).toContain('v-if="hint.isUnlocked && (hint.cost ?? 0) > 0"')
    expect(hints).toContain('<Button v-else-if="!hint.isUnlocked"')
    expect(hints).toContain("$t('ui.unlockHintPoints'")
  })

  test('competition and challenge editors share a live sanitized preview', async () => {
    const challenge = await sourceFile(new URL('../app/pages/admin/challenges/[id].vue', import.meta.url)).text()
    const competition = await sourceFile(new URL('../app/pages/admin/competitions/[id]/configuration.vue', import.meta.url)).text()
    const preview = await Bun.file(new URL('../app/components/ui/markdown/MarkdownPreview.vue', import.meta.url)).text()

    expect(challenge).toContain('<MarkdownPreview :source="form.description"')
    expect(competition).toContain('<MarkdownPreview :source="description"')
    expect(preview).toContain('renderMarkdownPreview(source)')
    expect(preview).toContain('sequence === renderSequence')
    expect(preview).toContain('requestAnimationFrame')
    expect(preview).toContain('<ScrollSurface axis="both"')
    const worker = await Bun.file(new URL('../app/workers/markdown.worker.ts', import.meta.url)).text()
    expect(worker).toContain('renderMarkdownAsync(source)')
    expect(worker).toContain('self.postMessage({ id, html }')
  })
})
