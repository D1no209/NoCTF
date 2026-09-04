import { describe, expect, test } from 'bun:test'
import type { NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse } from '../app/api'
import { adminRuntimeTeamLabel } from '../app/utils/admin-runtime'

const t = (source: string, values: Record<string, string | number> = {}) =>
  source.replace(/\{(\w+)\}/g, (match, key: string) =>
    values[key] === undefined ? match : String(values[key]))

function runtime(
  purpose: NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse['purpose'],
  overrides: Partial<NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse> = {},
): NoCtfapiEndpointsAdministrationRuntimeAdminRuntimeResponse {
  return { purpose, ...overrides }
}

describe('admin runtime team presentation', () => {
  test('shows the bound team for an AWDP attack runtime', () => {
    expect(adminRuntimeTeamLabel(runtime('AwdpAttack', {
      teamId: 'team-1',
      sourceTeamId: 'team-1',
      sourceTeamName: 'TEST TEAM1',
    }), t)).toBe('TEST TEAM1')
  })

  test('labels an AWDP target as one-time verification and includes attribution when available', () => {
    expect(adminRuntimeTeamLabel(runtime('AwdpTarget', {
      teamId: null,
      sourceTeamId: 'team-1',
      sourceTeamName: 'TEST TEAM1',
    }), t)).toBe('一次性 Fix 验证 Target · TEST TEAM1')

    expect(adminRuntimeTeamLabel(runtime('AwdpTarget', {
      teamId: null,
      sourceTeamId: null,
      sourceTeamName: null,
    }), t)).toBe('一次性 Fix 验证 Target')
  })

  test('keeps a true shared player runtime labelled as shared', () => {
    expect(adminRuntimeTeamLabel(runtime('Player', {
      teamId: null,
      sourceTeamId: null,
      sourceTeamName: null,
    }), t)).toBe('共享')
  })

  test('labels challenge-template test runtimes without inventing a team', () => {
    expect(adminRuntimeTeamLabel(runtime('TemplateTest', {
      teamId: null,
      sourceTeamId: null,
      sourceTeamName: null,
    }), t)).toBe('题目测试')
  })

  test('never mislabels an unbound AWDP attack runtime as shared', () => {
    expect(adminRuntimeTeamLabel(runtime('AwdpAttack', {
      teamId: null,
      sourceTeamId: null,
      sourceTeamName: null,
    }), t)).toBe('未绑定队伍的 AWDP 攻击环境')
  })
})
