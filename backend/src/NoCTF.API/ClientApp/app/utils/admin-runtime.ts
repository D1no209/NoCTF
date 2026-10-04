import type { NoCTFAPIEndpointsAdministrationRuntimeAdminRuntimeResponse } from '../api/models'

type Translate = (
  source: string,
  values?: Record<string, string | number>,
) => string

export function adminRuntimeTeamLabel(
  runtime: NoCTFAPIEndpointsAdministrationRuntimeAdminRuntimeResponse,
  t: Translate,
  lookupTeamName: (teamId: string) => string | undefined = () => undefined,
): string {
  const sourceTeamId = runtime.sourceTeamId ?? runtime.teamId
  const sourceTeamName = runtime.sourceTeamName
    ?? (sourceTeamId ? lookupTeamName(sourceTeamId) : undefined)

  if (runtime.purpose === 'AwdpTarget') {
    return sourceTeamName
      ? t("runtime.runtime.label.oneTimeFixVerification.adminRuntime", { team: sourceTeamName })
      : t("runtime.runtime.label.oneTimeFixVerification")
  }
  if (runtime.purpose === 'TemplateTest')
    return t("runtime.label.challengeTest")

  if (sourceTeamName)
    return sourceTeamName
  if (sourceTeamId)
    return sourceTeamId
  if (runtime.purpose === 'AwdpAttack')
    return t("runtime.runtime.description.awdpAttackRuntimeBound")
  return t("common.label.share")
}
