import type { PlatformLog } from '../src/api/platformLogs'
import { describe, expect, test } from 'bun:test'
import {
  DEFAULT_PLATFORM_LOG_LEVEL,
  matchesPlatformLog,
  PLATFORM_LOG_HUB_PATH,
} from '../src/components/admin/platform-logs/platformLogPresentation'
import en from '../src/locales/en.json'
import zhCN from '../src/locales/zh-CN.json'

const generatedSdk = await Bun.file(
  new URL('../src/api/generated/sdk.gen.ts', import.meta.url),
).text()
const generatedTypes = await Bun.file(
  new URL('../src/api/generated/types.gen.ts', import.meta.url),
).text()
const apiSource = await Bun.file(new URL('../src/api/platformLogs.ts', import.meta.url)).text()
const routerSource = await Bun.file(new URL('../src/router/index.ts', import.meta.url)).text()
const workspaceSource = await Bun.file(
  new URL('../src/components/admin/platform-logs/AdminPlatformLogsWorkspace.vue', import.meta.url),
).text()

describe('platform logs', () => {
  test('uses only generated strongly typed platform operations', () => {
    for (const operation of [
      'adminPlatformListLogs',
      'adminPlatformExportLogs',
      'adminPlatformListAuditLogs',
      'adminPlatformListDeadLetters',
      'adminPlatformRequeueDeadLetter',
    ]) {
      expect(generatedSdk).toContain(`export const ${operation}`)
      expect(apiSource).toContain(`${operation}(`)
    }

    expect(generatedTypes).toContain('url: \'/api/v1/admin/platform/logs\'')
    expect(generatedTypes).toContain('url: \'/api/v1/admin/platform/logs/export\'')
    expect(generatedTypes).toContain('url: \'/api/v1/admin/platform/audit-logs\'')
    expect(apiSource).not.toMatch(/https?:\/\//)
  })

  test('keeps the hub relative and protects the route with platform administrator metadata', () => {
    expect(PLATFORM_LOG_HUB_PATH).toBe('/hubs/v1/admin/platform-logs')
    expect(workspaceSource).toContain('hubUrl: PLATFORM_LOG_HUB_PATH')
    expect(workspaceSource).not.toMatch(/https?:\/\//)
    expect(routerSource).toContain('path: \'platform-logs\'')
    expect(routerSource).toContain('name: \'admin-platform-logs\'')
    expect(routerSource).toContain('requiresAdmin: true')
  })

  test('defaults to Warning and applies every live filter', () => {
    expect(DEFAULT_PLATFORM_LOG_LEVEL).toBe(3)
    const log: PlatformLog = {
      cursor: '1-0',
      timestamp: '2026-08-05T10:00:00Z',
      service: 2,
      level: 4,
      competitionId: 'competition-id',
      runtimeInstanceId: 'runtime-id',
      teamId: 'team-id',
      userId: 'user-id',
      competitionChallengeId: 'challenge-id',
      submissionId: 'submission-id',
      category: 'NoCTF.Runner',
      eventName: 'RuntimeFailed',
      message: 'Container startup failed',
    }
    const filter = {
      minimumLevel: 3,
      service: 2,
      from: '2026-08-05T09:00:00Z',
      to: '2026-08-05T11:00:00Z',
      category: 'NoCTF.Runner',
      search: 'STARTUP FAILED',
      competitionId: 'competition-id',
      runtimeInstanceId: 'runtime-id',
      teamId: 'team-id',
      userId: 'user-id',
      competitionChallengeId: 'challenge-id',
      submissionId: 'submission-id',
    } as const
    expect(matchesPlatformLog(log, filter)).toBe(true)
    expect(matchesPlatformLog(log, { ...filter, category: 'noctf.runner' })).toBe(true)
    for (const [key, value] of [
      ['category', 'another-category'],
      ['search', 'not-present'],
      ['competitionId', 'another-competition'],
      ['runtimeInstanceId', 'another-runtime'],
      ['teamId', 'another-team'],
      ['userId', 'another-user'],
      ['competitionChallengeId', 'another-challenge'],
      ['submissionId', 'another-submission'],
    ] as const) {
      expect(matchesPlatformLog(log, { ...filter, [key]: value })).toBe(false)
    }
    expect(matchesPlatformLog(log, {
      minimumLevel: 5,
      service: null,
      from: null,
      to: null,
      category: null,
      search: null,
      competitionId: null,
      runtimeInstanceId: null,
      teamId: null,
      userId: null,
      competitionChallengeId: null,
      submissionId: null,
    })).toBe(false)
  })

  test.each([
    ['en', en],
    ['zh-CN', zhCN],
  ])('documents the trusted-admin Flag boundary and all workspaces in %s', (_locale, messages) => {
    expect(messages.admin.nav.platformLogs).toBeTruthy()
    expect(messages.admin.platformLogs.tabs.live).toBeTruthy()
    expect(messages.admin.platformLogs.tabs.audit).toBeTruthy()
    expect(messages.admin.platformLogs.tabs.deadLetters).toBeTruthy()
    expect(messages.admin.platformLogs.redaction).toMatch(/Flag/)
    expect(messages.admin.platformLogs.retention).toBeTruthy()
    expect(messages.admin.platformLogs.export).toMatch(/JSONL/)
  })

  test('exposes bounded export and signed pagination controls', () => {
    expect(apiSource).toContain('parseAs: \'blob\'')
    expect(workspaceSource).toContain('platformLogsApi.export')
    expect(workspaceSource).toContain('auditNextCursor')
    expect(workspaceSource).toContain('fetchAudits(false)')
    expect(workspaceSource).toContain('competitionChallengeId')
    expect(workspaceSource).toContain('submissionId')
  })
})
