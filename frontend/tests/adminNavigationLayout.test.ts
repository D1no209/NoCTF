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
})
