import { normalizeMarkdownCodeLanguage } from './markdown-language'

export type CodeHighlighter = (code: string, language: string) => string | null

/** Escapes author text that is interpolated into the fence markup. */
export function escapeMarkdownFenceText(text: string): string {
  return text
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
}

/**
 * Renders one fenced code block for both the published renderer and the live
 * editor preview. The info string is normalized to a language token and the
 * markup carries no other author-controlled interpolation, so a fence info
 * string can never break out of the attributes.
 */
export function renderMarkdownFence(
  code: string,
  info: string | undefined,
  highlighter?: CodeHighlighter,
): string {
  const language = normalizeMarkdownCodeLanguage(info ?? '')
  const highlighted = highlighter?.(code, language)
  const displayLanguage = language || 'text'
  const languageClass = language ? ` language-${displayLanguage}` : ''
  const body = highlighted ?? escapeMarkdownFenceText(code)
  return `<pre tabindex="0" data-language="${displayLanguage}"><code class="hljs${languageClass}">${body}</code></pre>\n`
}
