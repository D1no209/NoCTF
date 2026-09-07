import { expect, test } from 'bun:test'
import { readFileSync } from 'node:fs'

const read = (path: string) => readFileSync(new URL(`../app/${path}`, import.meta.url), 'utf8')

test('school identity uses its own private SDK and string input without blocking participation', () => {
  const source = read('components/account/SchoolIdentityForm.vue')
  expect(source).toContain('authenticationGetMySchoolIdentity')
  expect(source).toContain('authenticationUpdateMySchoolIdentity')
  expect(source).toContain('type="text"')
  expect(source).not.toContain('type="number"')
  expect(source).not.toContain('localStorage')
  expect(source).toContain('if (!loaded.value || pending.value) return')
  expect(source).toContain('fieldErrors')
})

test('account workspace retains independent drafts, field errors and mobile layout', () => {
  const page = read('pages/account/index.vue')
  expect(page).toContain('orientation="vertical"')
  expect(page).toContain('md:flex-row')
  expect(page).not.toContain('<Card>')
  expect(page).toContain('value="school" force-mount')
  expect(page).toContain('if (!profileDirty.value)')
  expect(page).toContain('onBeforeRouteLeave')
  expect(page).toContain('profileError')
  expect(page).toContain('@submit.prevent="changePassword"')
})

test('private views are confined to staff pages and clear stale member responses', () => {
  expect(read('pages/admin/competitions/[id]/teams.vue')).toContain('v-if="canJudge && member.userId && selectedTeam.id"')
  expect(read('components/account/PrivateAccountPanel.vue')).toContain('if (ticket !== revision) return')
  expect(read('pages/users/[id].vue')).not.toContain('PrivateAccountPanel')
  expect(read('pages/competitions/[id]/teams/[teamId].vue')).not.toContain('PrivateAccountPanel')
})
