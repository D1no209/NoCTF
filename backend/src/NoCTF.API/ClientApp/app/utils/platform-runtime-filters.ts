import type { AdminPlatformListActiveRuntimesData } from '../api'

export interface PlatformRuntimeFilters {
  search: string
  scope: 'all' | 'Competition' | 'ChallengeTest'
  state: 'all' | 'Queued' | 'Provisioning' | 'Running' | 'Stopping'
  kind: 'all' | 'Container'
}

export function emptyPlatformRuntimeFilters(): PlatformRuntimeFilters {
  return { search: '', scope: 'all', state: 'all', kind: 'all' }
}

/** Snapshot only applied filters; editing the form must not change polling or cursor requests. */
export function platformRuntimeQuery(filters: PlatformRuntimeFilters): Omit<AdminPlatformListActiveRuntimesData['query'], 'cursor' | 'limit'> {
  return {
    search: filters.search.trim() || undefined,
    scope: filters.scope === 'all' ? undefined : filters.scope,
    state: filters.state === 'all' ? undefined : filters.state,
    runtimeKind: filters.kind === 'all' ? undefined : filters.kind,
    offset: 0,
    desc: true,
  }
}
