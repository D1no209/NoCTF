import { describe, expect, test } from 'bun:test'

const layoutSource = await Bun.file(new URL('../src/views/admin/AdminLayout.vue', import.meta.url)).text()

describe('admin navigation layout', () => {
  test('keeps available destinations grouped at the start of the sidebar', () => {
    const navigationMenu = layoutSource.match(/<SidebarContent[\s\S]*?<SidebarMenu class="([^"]+)"/)

    expect(navigationMenu).not.toBeNull()
    const classes = navigationMenu![1]!.split(/\s+/)
    expect(classes).not.toContain('h-full')
    expect(classes).not.toContain('justify-between')
  })

  test('uses the square brand mark when the sidebar is collapsed', () => {
    expect(layoutSource).toContain('group-data-[collapsible=icon]:px-2')
    expect(layoutSource).toContain('<BrandLogo class="h-10 group-data-[collapsible=icon]:hidden" />')
    expect(layoutSource).toContain('src="/favicon.svg"')
    expect(layoutSource).toContain('group-data-[collapsible=icon]:block')
  })

  test('groups administrator-only controls under platform configuration', () => {
    expect(layoutSource).toContain('to: \'/admin/settings/basic\'')
    expect(layoutSource).toContain('activePrefix: \'/admin/settings\'')
    expect(layoutSource).toContain('label: t(\'admin.nav.platformSettings\')')
    expect(layoutSource).not.toContain('to: \'/admin/email-verification\'')
  })

  test('places platform logs below health and before platform configuration', () => {
    const healthIndex = layoutSource.indexOf('to: \'/admin/health\'')
    const logsIndex = layoutSource.indexOf('to: \'/admin/platform-logs\'')
    const settingsIndex = layoutSource.indexOf('to: \'/admin/settings/basic\'')

    expect(healthIndex).toBeGreaterThan(-1)
    expect(logsIndex).toBeGreaterThan(healthIndex)
    expect(settingsIndex).toBeGreaterThan(logsIndex)
    expect(layoutSource).toContain('label: t(\'admin.nav.platformLogs\')')
  })
})
