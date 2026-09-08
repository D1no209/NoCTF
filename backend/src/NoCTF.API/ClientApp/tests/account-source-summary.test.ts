import { expect, test } from 'bun:test'
import { summarizeAccountSources } from '../app/utils/account-source-summary'
import { readFileSync } from 'node:fs'

test('common IPs summarize known addresses without exposing individual submissions', () => {
  const input = [
    { ipAddress: ' 2001:DB8::1 ', occurredAt: '2026-09-08T10:00:00Z' },
    { ipAddress: '2001:db8::1', occurredAt: '2026-09-08T11:00:00Z' },
    { ipAddress: null, occurredAt: '2026-09-08T12:00:00Z' },
    { ipAddress: ' ', occurredAt: null },
    { ipAddress: '192.0.2.1', occurredAt: 'invalid' },
  ]
  const before = structuredClone(input)
  expect(summarizeAccountSources(input)).toEqual([
    { address: '2001:db8::1', count: 2, lastSeen: '2026-09-08T11:00:00Z' },
    { address: '192.0.2.1', count: 1, lastSeen: null },
  ])
  expect(input).toEqual(before)
  expect(summarizeAccountSources([])).toEqual([])
})

test('common IPs are capped at three and sorted by frequency then recency', () => {
  const input = [
    { ipAddress: '192.0.2.1', occurredAt: '2026-09-08T01:00:00Z' },
    { ipAddress: '192.0.2.2', occurredAt: '2026-09-08T04:00:00Z' },
    { ipAddress: '192.0.2.3', occurredAt: '2026-09-08T03:00:00Z' },
    { ipAddress: '192.0.2.4', occurredAt: '2026-09-08T02:00:00Z' },
  ]
  expect(summarizeAccountSources(input).map(item => item.address)).toEqual(['192.0.2.2', '192.0.2.3', '192.0.2.4'])
})

test('platform user sheet prioritizes account information and hides the activity timeline', () => {
  const read = (path: string) => readFileSync(new URL(`../app/${path}`, import.meta.url), 'utf8')
  const page = read('pages/admin/platform/users.vue')
  expect(page).not.toContain('<Card')
  expect(page).toContain(':show-activities="false"')
  expect(page.indexOf('id="user-account-overview"')).toBeLessThan(page.indexOf('<PrivateAccountPanel'))
  expect(page.indexOf('<PrivateAccountPanel')).toBeLessThan(page.indexOf('id="user-account-management"'))
  expect(page).toContain('data-[side=right]:sm:max-w-2xl')
  expect(page).toContain('flex min-h-0 flex-1 flex-col gap-6 overflow-y-auto')
  for (const field of ['id', 'userName', 'email', 'kind', 'accountStatus', 'tokenVersion', 'createdAt', 'updatedAt']) {
    expect(page).toContain(`detail.${field}`)
  }
  const panel = read('components/account/PrivateAccountPanel.vue')
  expect(panel).toContain('showActivities: true')
  expect(panel).toContain('<template v-if="showActivities">')
  expect(read('components/account/SchoolIdentityForm.vue')).toContain("$t('个人信息')")
})
