import type { NoCtfapiEndpointsAdministrationGameplayFactsAdminPatchFailureCode } from '~/api'
import { translate } from './i18n'

const messages: Record<NoCtfapiEndpointsAdministrationGameplayFactsAdminPatchFailureCode, string> = {
  Forbidden: '只有本场比赛负责人、Manager、Judge 或平台管理员可以下载 Patch。',
  SubmissionNotFound: '本场比赛中不存在该提交。',
  NotFixSubmission: '只有 Fix 提交可以下载 Patch 包。',
  PatchNotFound: '该提交没有 Patch 包。',
  InvalidAssociation: 'Patch 包与提交的比赛、题目、队伍或提交人关联不一致。',
  FileNotFound: 'Patch 包文件已不存在。',
  StorageUnavailable: 'Patch 文件存储暂不可用，请稍后重试。',
  AuditUnavailable: 'Patch 下载审计保存失败，未下发文件，请稍后重试。',
}

export function adminPatchFailureMessage(code: NoCtfapiEndpointsAdministrationGameplayFactsAdminPatchFailureCode): string {
  return translate(messages[code])
}
