import type { NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse } from '../api'

type Translate = (
  source: string,
  values?: Record<string, string | number>,
) => string

export function adminRuntimeTeamLabel(
  runtime: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse,
  t: Translate,
  lookupTeamName: (teamId: string) => string | undefined = () => undefined,
): string {
  const sourceTeamId = runtime.sourceTeamId ?? runtime.teamId
  const sourceTeamName = runtime.sourceTeamName
    ?? (sourceTeamId ? lookupTeamName(sourceTeamId) : undefined)

  if (runtime.purpose === 'AwdpTarget') {
    return sourceTeamName
      ? t("ui.oneTimeFixVerificationTarget2", { team: sourceTeamName })
      : t("ui.oneTimeFixVerificationTarget")
  }
  if (runtime.purpose === 'TemplateTest')
    return t("ui.challengeTest")

  if (sourceTeamName)
    return sourceTeamName
  if (sourceTeamId)
    return sourceTeamId
  if (runtime.purpose === 'AwdpAttack')
    return t("ui.awdpAttackRuntimeWithoutABoundTeam")
  return t("ui.share")
}
