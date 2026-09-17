const languageName = /^[a-z\d][a-z\d_+.#-]{0,31}$/i

export function normalizeMarkdownCodeLanguage(info: string): string {
  const candidate = info.trim().split(/\s+/, 1)[0]?.toLowerCase() ?? ''
  return languageName.test(candidate) ? candidate : ''
}

export function hasMarkdownCodeFence(source: string): boolean {
  return /(?:^|\n)[ \t]{0,3}(?:`{3,}|~{3,})/.test(source)
}
