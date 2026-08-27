import { describe, expect, test } from 'bun:test'

const page = () => Bun.file(new URL('../app/pages/admin/competitions/[id]/challenges/[ccId].vue', import.meta.url)).text()

describe('admin competition challenge navigation', () => {
  test('places the challenge sections in a sticky right rail on desktop', async () => {
    const source = await page()

    expect(source).toContain('lg:grid-cols-[minmax(0,1fr)_14rem]')
    expect(source).toContain('lg:sticky lg:top-24 lg:col-start-2 lg:row-start-1 lg:flex-col')
    expect(source).toContain('value="general" class="mt-0 lg:col-start-1 lg:row-start-1"')
    expect(source).toContain('value="config" class="mt-0 lg:col-start-1 lg:row-start-1"')
    expect(source).toContain('value="hints" class="mt-0 lg:col-start-1 lg:row-start-1"')
  })
})
