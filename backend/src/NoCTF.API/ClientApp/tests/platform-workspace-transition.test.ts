import { expect, test } from 'bun:test'
import { sourceFile } from './support/feature-source'

test('platform sections use the stable competition-style content swap', async () => {
  const shell = await sourceFile(
    new URL('../app/pages/admin/platform.vue', import.meta.url),
  ).text()

  expect(shell).toContain('const activePath = computed(() => adminWorkspacePath(route.path))')
  expect(shell).toContain('<MotionSwap :identity="activePath" preset="film-up">')
  expect(shell).toContain('<NuxtPage />')
  expect(shell).toContain('data-platform-workspace-content')
  expect(shell).toContain('data-workspace-scroll-content')
  expect(shell).toContain('<ScrollSurface axis="y"')
  expect(shell).toContain('min-h-0 flex-1 overscroll-contain')
})

test('platform information and configuration share one continuous card', async () => {
  const overview = await sourceFile(
    new URL('../app/pages/admin/platform/index.vue', import.meta.url),
  ).text()

  expect(overview.match(/<Card(?:\s|>)/g)).toHaveLength(1)
  expect(overview).toContain('id="platform-runtime-information"')
  expect(overview).toContain('id="platform-identity-configuration"')
  expect(overview).toContain('<Separator />')
})

test('audit export shares the existing filter action row', async () => {
  const audit = await sourceFile(
    new URL('../app/pages/admin/platform/audit.vue', import.meta.url),
  ).text()

  expect(audit).toContain("$t('administration.label.downloadAuditArchive')")
  expect(audit).not.toContain("$t('common.label.auditDataExport')")
  expect(audit).not.toContain("$t('common.description.generateAuditArchiveSynchronously')")
})
