import type { InjectionKey } from 'vue'
import type { NoCtfapiEndpointsCompetitionsCompetitionResponse } from '~/api'

/**
 * My role inside one competition, derived in the [id] shell:
 * - owner: competition owner or platform administrator (full access incl. permissions)
 * - manager: can moderate (read + write)
 * - judge / observer: read-only UI
 */
export type CompetitionAdminRole = 'owner' | 'manager' | 'judge' | 'observer'

export interface CompetitionAdminContext {
  competitionId: string
  competition: Ref<NoCtfapiEndpointsCompetitionsCompetitionResponse | null>
  role: Ref<CompetitionAdminRole>
  /** owner or manager: show write controls. */
  canWrite: ComputedRef<boolean>
  /** owner, manager, or judge: show adjudication and team-ban controls. */
  canJudge: ComputedRef<boolean>
  /** owner or platform administrator: show permission/ownership controls. */
  canManagePermissions: ComputedRef<boolean>
  refresh: () => Promise<void>
}

export const CompetitionAdminKey: InjectionKey<CompetitionAdminContext> = Symbol('competition-admin')

export function useCompetitionAdmin(): CompetitionAdminContext {
  const ctx = inject(CompetitionAdminKey)
  if (!ctx) throw new Error('useCompetitionAdmin must be used under the competition admin shell')
  return ctx
}
