export function tagKey(name: string): string {
  return name.trim().toUpperCase()
}

export function uniqueTags(names: readonly string[]): string[] {
  const tags = new Map<string, string>()
  for (const name of names) {
    const trimmed = name.trim()
    if (trimmed && !tags.has(tagKey(trimmed))) tags.set(tagKey(trimmed), trimmed)
  }
  return [...tags.values()]
}

export function validChallengeTags(names: readonly string[]): boolean {
  return names.every(name => !!name.trim() && name.trim().length <= 40)
    && uniqueTags(names).length <= 20
}

export function challengeTagOptions(items: readonly { tags?: readonly string[] }[]): string[] {
  return uniqueTags(items.flatMap(item => item.tags ?? []))
    .sort((left, right) => tagKey(left).localeCompare(tagKey(right)))
}

export function matchesAllTags(tags: readonly string[], selected: readonly string[]): boolean {
  const keys = new Set(tags.map(tagKey))
  return selected.every(tag => keys.has(tagKey(tag)))
}

export function tagsFromQuery(value: unknown): string[] {
  return uniqueTags((Array.isArray(value) ? value : [value])
    .filter((item): item is string => typeof item === 'string'))
}
