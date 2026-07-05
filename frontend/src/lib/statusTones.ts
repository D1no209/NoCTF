export type SemanticBadgeVariant =
  | 'default'
  | 'secondary'
  | 'destructive'
  | 'outline'
  | 'success'
  | 'warning'
  | 'info'
  | 'attack'
  | 'defense'
  | 'neutral'

function key(value?: string | number | null) {
  return String(value ?? '').trim().toLowerCase()
}

export function competitionStatusVariant(status?: string | null): SemanticBadgeVariant {
  const s = key(status)
  if (s === 'running' || s === 'active') return 'success'
  if (s === 'published' || s === 'upcoming') return 'info'
  if (s === 'draft' || s === 'pending' || s === 'paused') return 'warning'
  if (s === 'finished' || s === 'ended' || s === 'closed') return 'neutral'
  if (s === 'cancelled' || s === 'failed' || s === 'rejected') return 'destructive'
  return 'outline'
}

export function registrationStatusVariant(status?: string | null): SemanticBadgeVariant {
  const s = key(status)
  if (s === 'approved') return 'success'
  if (s === 'rejected' || s === 'banned') return 'destructive'
  if (s === 'pending' || s === 'reviewing') return 'warning'
  return 'neutral'
}

export function runtimeStatusVariant(status?: string | number | null): SemanticBadgeVariant {
  const s = key(status)
  if (s.includes('success') || s.includes('healthy') || s.includes('running') || s === 'ok' || s === 'active' || s === 'applied' || s === 'verified')
    return 'success'
  if (s.includes('uploading') || s.includes('checking') || s.includes('auditing') || s.includes('submitted') || s.includes('retrying'))
    return 'info'
  if (s.includes('created') || s.includes('pending') || s.includes('paused') || s.includes('queued') || s.includes('notstarted'))
    return 'warning'
  if (s.includes('error') || s.includes('failed') || s.includes('timeout') || s.includes('expired') || s.includes('dead') || s.includes('exhausted') || s.includes('rejected'))
    return 'destructive'
  if (s.includes('stopped') || s.includes('destroyed') || s.includes('unknown') || s.includes('notcreated'))
    return 'neutral'
  return 'outline'
}

export function gameModeVariant(mode?: string | null): SemanticBadgeVariant {
  const s = key(mode)
  if (s === 'awd') return 'attack'
  if (s === 'awdp') return 'defense'
  if (s === 'koh') return 'warning'
  return 'info'
}

export function userRoleVariant(role?: string | null): SemanticBadgeVariant {
  const s = key(role)
  if (s === 'admin') return 'destructive'
  if (s === 'organizer') return 'info'
  return 'neutral'
}
