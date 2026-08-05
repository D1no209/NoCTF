import { afterAll, beforeEach, describe, expect, test } from 'bun:test'
import { dataExportApi, DataExportStatus } from '../src/api/dataExports'
import { client } from '../src/api/generated/client.gen'
import en from '../src/locales/en.json'
import zhCN from '../src/locales/zh-CN.json'

const apiBaseUrl = 'https://api.noctf.test'
const competitionId = '11111111-1111-1111-1111-111111111111'
const exportId = '22222222-2222-2222-2222-222222222222'
const originalClientConfig = client.getConfig()
const requests: Request[] = []
const exportJob = {
  id: exportId,
  scope: 0,
  competitionId,
  requestedByUserId: '33333333-3333-3333-3333-333333333333',
  requestedAt: '2026-08-06T00:00:00Z',
  includeProtectedFlags: false,
  status: DataExportStatus.Available,
  fileName: 'competition.zip',
  length: 2048,
  expiresAt: '2026-08-07T00:00:00Z',
}

const contractFetch: typeof fetch = async (input, init) => {
  const normalizedInput = typeof input === 'string' && input.startsWith('/')
    ? new URL(input, apiBaseUrl)
    : input
  const request = new Request(normalizedInput, init)
  requests.push(request)
  const pathname = new URL(request.url).pathname

  if (pathname === `/api/v1/admin/competitions/${competitionId}/data-exports`) {
    return request.method === 'POST'
      ? Response.json({ ...exportJob, status: DataExportStatus.Queued }, { status: 202 })
      : Response.json({ items: [exportJob] })
  }
  if (pathname === '/api/v1/admin/platform/audit-logs/data-exports') {
    return request.method === 'POST'
      ? Response.json({ ...exportJob, scope: 1, competitionId: null }, { status: 202 })
      : Response.json({ items: [{ ...exportJob, scope: 1, competitionId: null }] })
  }
  if (pathname === `/api/v1/admin/data-exports/${exportId}/download`) {
    return new Response(new Blob(['archive']), {
      headers: { 'Content-Disposition': 'attachment; filename="competition.zip"' },
    })
  }
  return Response.json({}, { status: 404 })
}

beforeEach(() => {
  requests.length = 0
  client.setConfig({ baseUrl: apiBaseUrl, fetch: contractFetch })
})

afterAll(() => client.setConfig(originalClientConfig))

describe('data exports', () => {
  test('uses generated competition and platform audit operations', async () => {
    await expect(dataExportApi.listCompetition(competitionId)).resolves.toEqual([exportJob])
    await expect(dataExportApi.createCompetition(
      competitionId,
      true,
      'Appeal evidence review',
    )).resolves.toMatchObject({ status: DataExportStatus.Queued })
    await expect(dataExportApi.listPlatformAudit()).resolves.toHaveLength(1)
    await expect(dataExportApi.createPlatformAudit()).resolves.toMatchObject({ scope: 1 })

    expect(requests.map(request => new URL(request.url).pathname)).toEqual([
      `/api/v1/admin/competitions/${competitionId}/data-exports`,
      `/api/v1/admin/competitions/${competitionId}/data-exports`,
      '/api/v1/admin/platform/audit-logs/data-exports',
      '/api/v1/admin/platform/audit-logs/data-exports',
    ])
    expect(await requests[1]!.clone().json()).toEqual({
      includeProtectedFlags: true,
      reason: 'Appeal evidence review',
    })
  })

  test('downloads with the generated operation and server filename', async () => {
    const result = await dataExportApi.download(exportId)
    expect(result.content).toBeInstanceOf(Blob)
    expect(result.fileName).toBe('competition.zip')
    expect(new URL(requests[0]!.url).pathname)
      .toBe(`/api/v1/admin/data-exports/${exportId}/download`)
  })

  test('keeps URLs in generated code and exposes complete localized states', async () => {
    const apiSource = await Bun.file(new URL('../src/api/dataExports.ts', import.meta.url)).text()
    const panelSource = await Bun.file(new URL(
      '../src/components/admin/data-exports/AdminDataExportsPanel.vue',
      import.meta.url,
    )).text()
    expect(apiSource).not.toContain('/api/v1')
    expect(apiSource).not.toMatch(/https?:\/\//)
    expect(panelSource).toContain('DataExportStatus.Processing')
    expect(panelSource).toContain('includeProtectedFlags')
    expect(panelSource).toContain('failureLabel')
    for (const messages of [en, zhCN]) {
      expect(messages.admin.dataExports.competitionTitle).toBeTruthy()
      expect(messages.admin.dataExports.auditTitle).toBeTruthy()
      expect(messages.admin.dataExports.statuses.failed).toBeTruthy()
      expect(messages.notifications.events.dataExportReady.title).toBeTruthy()
      expect(messages.notifications.events.dataExportFailed.title).toBeTruthy()
    }
  })
})
