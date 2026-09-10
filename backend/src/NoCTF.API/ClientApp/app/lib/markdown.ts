import MarkdownIt from 'markdown-it'
import sanitizeHtml from 'sanitize-html'
import { highlightMarkdownCode, normalizeMarkdownCodeLanguage } from './markdown-highlighter'

// HTML must pass the allowlist below after Markdown parsing and before v-html.
const markdown = new MarkdownIt({ html: true, linkify: true })
markdown.renderer.rules.fence = (tokens, index) => {
  const token = tokens[index]!
  const language = normalizeMarkdownCodeLanguage(token.info)
  const highlighted = highlightMarkdownCode(token.content, language)
    ?? markdown.utils.escapeHtml(token.content)
  const displayLanguage = language || 'text'
  const languageClass = language ? ` language-${displayLanguage}` : ''
  return `<pre tabindex="0" data-language="${displayLanguage}"><code class="hljs${languageClass}">${highlighted}</code></pre>\n`
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
  allowedTags: [...sanitizeHtml.defaults.allowedTags, 'img', 'details', 'summary', 'del', 'ins', 'audio', 'video', 'source', 'picture'],
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
    pre: (tagName, attribs) => ({ tagName, attribs: { ...attribs, tabindex: '0' } }),
    table: (tagName, attribs) => ({ tagName, attribs: { ...attribs, tabindex: '0' } }),
  },
  exclusiveFilter: frame => frame.tag === 'img' && !frame.attribs.src,
  nonTextTags: ['script', 'style', 'textarea', 'option', 'iframe', 'object', 'svg', 'math', 'template'],
}

/** Sanitize the complete result, including Markdown-generated links and author HTML. */
export function renderMarkdown(source: string): string {
  return sanitizeHtml(markdown.render(source), htmlOptions)
}
