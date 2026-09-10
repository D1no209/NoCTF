import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'

describe('competition countdown', () => {
  test('refreshes the overview countdown every second and disposes its timer', async () => {
    const source = (await sourceFile(
      new URL('../app/features/competitions/CompetitionOverview.vue', import.meta.url),
    ).text()).replaceAll('\r\n', '\n')

    expect(source).toMatch(/timer = setInterval\(\(\) => \{\n\s+now\.value = Date\.now\(\)\n\s+\}, 1_000\)/)
    expect(source).toContain('onUnmounted(() => clearInterval(timer))')
  })
})
