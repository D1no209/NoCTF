import MarkdownIt from 'markdown-it'
import sanitizeHtml from 'sanitize-html'
import { hasMarkdownCodeFence, normalizeMarkdownCodeLanguage } from './markdown-language'
import { renderMarkdownFence, type CodeHighlighter } from './markdown-fence'

function createMarkdown(highlighter?: CodeHighlighter) {
  const markdown = new MarkdownIt({ html: true, linkify: true })
  markdown.renderer.rules.fence = (tokens, index) => {
    const token = tokens[index]!
    return renderMarkdownFence(token.content, token.info, highlighter)
  }
  return markdown
}

// HTML must pass the allowlist below after Markdown parsing and before display.
const plainMarkdown = createMarkdown()
let highlightedMarkdown: ReturnType<typeof createMarkdown> | undefined

/** Top-level author HTML blocks, using the same parser as the published renderer. */
export function markdownHtmlBlockRanges(source: string): Array<{ fromLine: number; toLine: number }> {
  if (!source.includes('<')) return []
  return plainMarkdown.parse(source, {}).flatMap(token =>
    token.type === 'html_block' && token.level === 0 && token.map
      ? [{ fromLine: token.map[0] + 1, toLine: token.map[1] }]
      : [])
}

const color = /^(?:#[\da-f]{3,8}|[a-z]+|(?:rgb|hsl)a?\([\d\s.,%/+-]+\))$/i
const length = /^(?:0|auto|\d{1,4}(?:\.\d+)?(?:px|em|rem|%))$/i
const spacing = /^(?:0|auto|\d{1,3}(?:\.\d+)?(?:px|em|rem|%))(?:\s+(?:0|auto|\d{1,3}(?:\.\d+)?(?:px|em|rem|%))){0,3}$/i
const alignment: sanitizeHtml.Transformer = (tagName, attribs) => {
  const { align, ...attributes } = attribs
  if (align && /^(left|center|right|justify)$/i.test(align.trim()))
    attributes.style = `${attributes.style ?? ''};text-align:${align.trim().toLowerCase()}`
  return { tagName, attribs: attributes }
}

const htmlOptions: sanitizeHtml.IOptions = {
  // Keep this product policy explicit: dependency updates must not widen author HTML.
  allowedTags: [
    'address', 'article', 'aside', 'footer', 'header', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6',
    'hgroup', 'main', 'nav', 'section', 'blockquote', 'dd', 'div', 'dl', 'dt', 'figcaption',
    'figure', 'hr', 'li', 'menu', 'ol', 'p', 'pre', 'ul', 'a', 'abbr', 'b', 'bdi', 'bdo',
    'br', 'cite', 'code', 'data', 'dfn', 'em', 'i', 'kbd', 'mark', 'q', 'rb', 'rp', 'rt',
    'rtc', 'ruby', 's', 'samp', 'small', 'span', 'strong', 'sub', 'sup', 'time', 'u', 'var',
    'wbr', 'caption', 'col', 'colgroup', 'table', 'tbody', 'td', 'tfoot', 'th', 'thead', 'tr',
    'img', 'details', 'summary', 'del', 'ins', 'audio', 'video', 'source', 'picture',
  ],
  disallowedTagsMode: 'discard',
  allowedAttributes: {
    '*': ['style', 'title', 'lang', { name: 'dir', values: ['ltr', 'rtl', 'auto'] }],
    a: ['href', 'rel', { name: 'target', values: ['_blank'] }],
    img: ['src', 'alt', 'width', 'height', 'loading', 'referrerpolicy'],
    code: ['class'],
    span: ['class'],
    pre: [{ name: 'tabindex', values: ['0'] }, 'data-language'],
    table: [{ name: 'tabindex', values: ['0'] }],
    th: ['colspan', 'rowspan', 'scope'],
    td: ['colspan', 'rowspan'],
    col: ['span'], colgroup: ['span'],
    ol: ['start', 'reversed', { name: 'type', values: ['1', 'a', 'A', 'i', 'I'] }],
    li: ['value'],
    details: ['open'],
    time: ['datetime'],
    audio: ['src', 'controls', 'preload'],
    video: ['src', 'poster', 'width', 'height', 'controls', 'preload', 'playsinline'],
    source: ['src', 'type', 'media'],
  },
  allowedClasses: {
    code: ['hljs', /^language-[a-z\d_+.#-]+$/i],
    span: [/^hljs-[a-z\d_-]+$/i],
  },
  allowedStyles: {
    '*': {
      'text-align': [/^(left|center|right|justify|start|end)$/i],
      'vertical-align': [/^(baseline|middle|top|bottom|text-top|text-bottom|sub|super)$/i],
      color: [color], 'background-color': [color],
      'font-weight': [/^(normal|bold|bolder|lighter|[1-9]00)$/i],
      'font-style': [/^(normal|italic|oblique)$/i],
      'font-size': [/^(?:\d{1,2}(?:\.\d+)?px|[0-3](?:\.\d+)?(?:em|rem)|\d{1,3}%)$/i],
      'text-decoration': [/^(none|underline|overline|line-through)$/i],
      'line-height': [/^(normal|[0-3](?:\.\d+)?|\d{1,3}%)$/i],
      width: [length], height: [length], 'max-width': [length],
      margin: [spacing], padding: [spacing],
      'margin-left': [length], 'margin-right': [length],
      'border-width': [/^\d{1,2}px$/], 'border-style': [/^(none|solid|dashed|dotted|double)$/i],
      'border-color': [color], 'border-radius': [length],
    },
  },
  allowedSchemes: ['http', 'https', 'mailto', 'tel', 'ftp'],
  allowedSchemesByTag: { img: ['http', 'https'], audio: ['http', 'https'], video: ['http', 'https'], source: ['http', 'https'] },
  allowedSchemesAppliedToAttributes: ['href', 'src', 'poster'],
  transformTags: {
    center: (_tagName, attribs) => alignment('div', { ...attribs, align: 'center' }),
    div: alignment, p: alignment, h1: alignment, h2: alignment, h3: alignment,
    h4: alignment, h5: alignment, h6: alignment, th: alignment, td: alignment,
    a: (tagName, attribs) => ({ tagName, attribs: {
      ...attribs, rel: 'noopener noreferrer',
      ...(/^(https?:)?\/\//i.test(attribs.href ?? '') ? { target: '_blank' } : {}),
    } }),
    img: (tagName, attribs) => ({ tagName, attribs: { ...attribs, loading: 'lazy', referrerpolicy: 'no-referrer' } }),
    audio: (tagName, attribs) => ({ tagName, attribs: { ...attribs, controls: '', preload: 'none' } }),
    video: (tagName, attribs) => ({ tagName, attribs: { ...attribs, controls: '', preload: 'none' } }),
    // Author HTML may carry its own pre/data-language; normalize it so the CSS label cannot be spoofed.
    pre: (tagName, attribs) => {
      const attributes: Record<string, string> = { ...attribs, tabindex: '0' }
      if ('data-language' in attributes)
        attributes['data-language'] = normalizeMarkdownCodeLanguage(attributes['data-language'] ?? '') || 'text'
      return { tagName, attribs: attributes }
    },
    table: (tagName, attribs) => ({ tagName, attribs: { ...attribs, tabindex: '0' } }),
  },
  exclusiveFilter: frame => frame.tag === 'img' && !frame.attribs.src,
  nonTextTags: ['script', 'style', 'textarea', 'option', 'iframe', 'object', 'svg', 'math', 'template'],
}

/** 渲染层只允许重建清洗白名单认可的标签(白名单变化时自动跟随)。 */
export const sanitizedMarkdownTags: ReadonlySet<string> = new Set(htmlOptions.allowedTags as string[])

/** Sanitize the complete result, including Markdown-generated links and author HTML. */
export function renderMarkdown(source: string, highlighter?: CodeHighlighter, assets?: Readonly<Record<string, string>>): string {
  if (highlighter && !highlightedMarkdown)
    highlightedMarkdown = createMarkdown(highlighter)
  const options: sanitizeHtml.IOptions = assets === undefined ? htmlOptions : {
    ...htmlOptions,
    allowedTags: (htmlOptions.allowedTags as string[]).filter(tag => !['audio', 'video', 'source', 'picture'].includes(tag)),
    allowedSchemesByTag: { img: ['blob'] },
    transformTags: {
      ...htmlOptions.transformTags,
      img: (_tagName, attribs) => {
        const asset = assets[attribs.src ?? '']
        return asset?.startsWith('blob:')
          ? { tagName: 'img', attribs: { src: asset, alt: attribs.alt ?? '', loading: 'lazy', referrerpolicy: 'no-referrer' } }
          : { tagName: 'span', attribs: {} as Record<string, string> }
      },
    },
  }
  return sanitizeHtml((highlighter ? highlightedMarkdown! : plainMarkdown).render(source), options)
}

/** Loads Highlight.js only for documents that contain a code fence. */
export async function renderMarkdownAsync(source: string, assets?: Readonly<Record<string, string>>): Promise<string> {
  if (!hasMarkdownCodeFence(source))
    return renderMarkdown(source, undefined, assets)
  try {
    const { highlightMarkdownCode } = await import('./markdown-highlighter')
    return renderMarkdown(source, highlightMarkdownCode, assets)
  }
  catch {
    // A missing or stale highlighter chunk must degrade to plain code, never to a blank document.
    return renderMarkdown(source, undefined, assets)
  }
}

interface MarkdownCacheEntry {
  source: string
  html: Promise<string>
}

const publishedMarkdownCache = new Map<string, MarkdownCacheEntry>()
const publishedMarkdownCacheLimit = 64

function markdownSourceHash(source: string): string {
  let hash = 2166136261
  for (let index = 0; index < source.length; index++) {
    hash ^= source.charCodeAt(index)
    hash = Math.imul(hash, 16777619)
  }
  return `${source.length}:${(hash >>> 0).toString(36)}`
}

/** Reuses parsed and sanitized HTML for immutable published content. */
export function renderPublishedMarkdown(source: string): Promise<string> {
  const key = markdownSourceHash(source)
  const cached = publishedMarkdownCache.get(key)
  if (cached?.source === source) {
    publishedMarkdownCache.delete(key)
    publishedMarkdownCache.set(key, cached)
    return cached.html
  }

  const pending = renderMarkdownAsync(source)
  const entry: MarkdownCacheEntry = { source, html: pending }
  // A failed render must not stay cached as a permanent rejection for this source.
  entry.html = pending.catch((error: unknown) => {
    if (publishedMarkdownCache.get(key) === entry)
      publishedMarkdownCache.delete(key)
    throw error
  })
  publishedMarkdownCache.set(key, entry)
  if (publishedMarkdownCache.size > publishedMarkdownCacheLimit)
    publishedMarkdownCache.delete(publishedMarkdownCache.keys().next().value!)
  return entry.html
}
