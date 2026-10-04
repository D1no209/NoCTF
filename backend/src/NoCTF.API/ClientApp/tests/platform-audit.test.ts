import { describe, expect, test } from 'bun:test'
import type { NoCTFAPIEndpointsAdministrationPlatformPlatformAuditLogResponse } from '../app/api/models'
import { platformAuditActionText } from '../app/utils/platform-audit'

function audit(overrides: Partial<NoCTFAPIEndpointsAdministrationPlatformPlatformAuditLogResponse>) {
  return {
    id: 'audit-1',
    kind: 'CompetitionEvent',
    subjectId: 'competition-1',
    automatic: false,
    occurredAt: '2026-08-22T00:00:00Z',
    ...overrides,
  } as NoCTFAPIEndpointsAdministrationPlatformPlatformAuditLogResponse
}

describe('platform audit operation labels', () => {
  test('names competition creation and deletion instead of showing an empty reason', () => {
    expect(platformAuditActionText(audit({ competitionEventKind: 'CompetitionCreated' })))
      .toBe('创建竞赛')
    expect(platformAuditActionText(audit({ competitionEventKind: 'CompetitionDeleted' })))
      .toBe('删除竞赛')
  })

  test('translates internal lifecycle reasons into an operator action', () => {
    expect(platformAuditActionText(audit({
      kind: 'CompetitionLifecycle',
      reason: 'manual_start',
      fromCompetitionStatus: 'Published',
      toCompetitionStatus: 'Running',
    }))).toBe('启动竞赛')
  })

  test('keeps a human reason for destructive actions', () => {
    expect(platformAuditActionText(audit({
      kind: 'CompetitionAdministration',
      reason: '测试数据清理',
    }))).toBe('强制级联删除竞赛 · 原因：测试数据清理')
  })

  test('names account activation explicitly', () => {
    expect(platformAuditActionText(audit({
      kind: 'UserAccountLifecycle',
      userAccountAction: 'Activated',
      reason: 'manual_activate',
    }))).toBe('激活账户')
  })

  test('names administrator token issuance and revocation actions', () => {
    expect(platformAuditActionText(audit({
      kind: 'PlatformAdministration',
      platformAdministrationAction: 'UserAccessTokenIssued',
      reason: 'support case',
    }))).toBe('签发用户访问 JWT · 原因：support case')
    expect(platformAuditActionText(audit({
      kind: 'PlatformAdministration',
      platformAdministrationAction: 'UserAccessTokenRevoked',
    }))).toBe('吊销用户访问 JWT')
    expect(platformAuditActionText(audit({
      kind: 'PlatformAdministration',
      platformAdministrationAction: 'UserTokensInvalidated',
    }))).toBe('撤销用户全部 JWT')
  })
})
