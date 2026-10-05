import type { InjectionKey } from 'vue'
import type {
  NoCtfapiEndpointsCompetitionsCompetitionAdministrationRoleProtocol,
  NoCtfapiEndpointsCompetitionsCompetitionResponse,
} from '../api'

/**
 * My role inside one competition, derived in the [id] shell:
 * - Owner: competition owner or platform administrator (full access incl. permissions)
 * - Manager: can moderate (read + write)
 * - Judge: can adjudicate incidents, bans, and ban appeals
 * - Observer: read-only UI
 */
export type CompetitionAdminRole = NoCtfapiEndpointsCompetitionsCompetitionAdministrationRoleProtocol

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
