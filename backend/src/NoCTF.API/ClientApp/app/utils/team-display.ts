type TeamIdentity = {
  id?: string | null
  teamId?: string | null
  name?: string | null
  teamName?: string | null
}

function identity(item: TeamIdentity): { id: string; name: string } {
  return {
    id: item.id ?? item.teamId ?? '',
    name: (item.name ?? item.teamName ?? '').trim(),
  }
}

export function buildTeamDisplayNames(items: readonly TeamIdentity[]): ReadonlyMap<string, string> {
  const identities = items.map(identity)
  const counts = new Map<string, number>()
  for (const item of identities) {
    const key = item.name.toLocaleLowerCase()
    counts.set(key, (counts.get(key) ?? 0) + 1)
  }

  return new Map(identities.map((item) => {
    const duplicate = (counts.get(item.name.toLocaleLowerCase()) ?? 0) > 1
    const suffix = duplicate && item.id ? ` · ${item.id.slice(0, 8)}` : ''
    return [item.id, `${item.name}${suffix}`]
  }))
}

export function teamDisplayName(
  item: TeamIdentity,
  displayNames: ReadonlyMap<string, string>,
): string {
  const value = identity(item)
  return displayNames.get(value.id) ?? value.name
}
