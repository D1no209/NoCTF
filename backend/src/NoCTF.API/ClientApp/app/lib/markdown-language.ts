const languageName = /^[a-z\d][a-z\d_+.#-]{0,31}$/i
/**
 * Fences may be nested in blockquotes and lists, so detection has to skip the
 * container markers instead of only matching a line-start fence.
 */
const fenceLine = /(?:^|\n)[ \t]*(?:>[ \t]*)*(?:`{3,}|~{3,})/

export function normalizeMarkdownCodeLanguage(info: string): string {
  const candidate = info.trim().split(/\s+/, 1)[0]?.toLowerCase() ?? ''
  return languageName.test(candidate) ? candidate : ''
}

export function hasMarkdownCodeFence(source: string): boolean {
  return fenceLine.test(source)
}
