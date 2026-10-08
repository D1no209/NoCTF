import { expect, test } from 'bun:test'
import { createMockApi } from './api'
import { id } from './schema'

test('single-writeup demo withholds body before explicit unlock and hands PDFs to cookie-authorized browser requests', async () => {
  const api = createMockApi(), origin = 'http://127.0.0.1:5081'
  const login = await api.handle(new Request(origin + '/api/v1/auth/login', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ login: 'player', password: 'Mock123!' }) }))
  const { accessToken } = await login.json()
  const send = (path: string, method = 'GET', body?: object) => api.handle(new Request(origin + path, { method,
    headers: { Authorization: `Bearer ${accessToken}`, 'Content-Type': 'application/json' }, ...(body ? { body: JSON.stringify(body) } : {}) }))
  const topic = `/api/v1/competitions/${id(2)}/challenges/${id(4)}/writeups`
  const list = await (await send(topic + '?staff=false')).json()
  expect(JSON.stringify(list)).not.toContain('flag{mock_success}')
  const markdown = list.items.find((x: { published: { format: string } }) => x.published.format === 'Markdown')
  const version = `${topic}/versions/${markdown.publishedVersionId}`
  expect((await send(version)).status).toBe(403)
  const quote = await (await send(version + '/quote')).json()
  expect((await send(version + '/unlock', 'POST', { policyStamp: quote.policyStamp })).status).toBe(200)
  expect((await (await send(version)).json()).markdown).toContain('flag{mock_success}')
  const pdf = list.items.find((x: { published: { format: string } }) => x.published.format === 'Pdf')
  const handoff = await send(`${topic}/versions/${pdf.publishedVersionId}/browser-access`, 'POST', { staff: false })
  const cookie = handoff.headers.get('Set-Cookie')
  expect(cookie).toContain('HttpOnly')
  const urls = await handoff.json()
  expect(urls.downloadUrl).not.toContain(accessToken)
  const browserRead = await api.handle(new Request(origin + urls.downloadUrl, { headers: { Cookie: cookie!.split(';')[0]! } }))
  expect(browserRead.status).toBe(200); expect(browserRead.headers.get('Content-Type')).toBe('application/pdf')
  expect((await browserRead.text()).startsWith('%PDF-')).toBeTrue()
})
