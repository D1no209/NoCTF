import type { NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse } from '~/api'

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
      ? t('一次性 Fix 验证 Target · {team}', { team: sourceTeamName })
      : t('一次性 Fix 验证 Target')
  }
  if (runtime.purpose === 'TemplateTest')
    return t('题目测试')

  if (sourceTeamName)
    return sourceTeamName
  if (sourceTeamId)
    return sourceTeamId
  if (runtime.purpose === 'AwdpAttack')
    return t('未绑定队伍的 AWDP 攻击环境')
  return t('共享')
}
