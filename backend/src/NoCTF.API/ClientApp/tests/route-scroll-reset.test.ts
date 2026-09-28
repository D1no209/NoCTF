import { expect, test } from 'bun:test'
import { sourceFile } from './support/feature-source'

test('persistent route scroll surfaces reset when their rendered path changes', async () => {
  const scrollSurface = await Bun.file(
    new URL('../app/components/ui/scroll-area/ScrollSurface.vue', import.meta.url),
  ).text()
  const layout = await sourceFile(
    new URL('../app/layouts/default.vue', import.meta.url),
  ).text()
  const platform = await sourceFile(
    new URL('../app/pages/admin/platform.vue', import.meta.url),
  ).text()
  const competition = await sourceFile(
    new URL('../app/pages/admin/competitions/[id].vue', import.meta.url),
  ).text()

  expect(scrollSurface).toContain('resetKey?: string | number | null')
  expect(scrollSurface).toContain('watch(() => props.resetKey')
  expect(scrollSurface).toContain('await nextTick()')
  expect(scrollSurface).toContain('target.value.scrollTop = 0')
  expect(layout).toContain(':reset-key="routePath"')
  expect(platform).toContain(':reset-key="activePath"')
  expect(competition).toContain(':reset-key="activePath"')
})
