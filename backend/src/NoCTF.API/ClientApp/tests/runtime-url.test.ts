import { sourceFile } from './support/feature-source'
import { describe, expect, test } from 'bun:test'
import { isRuntimeUrlClickable } from '../app/utils/runtime-url'

describe('runtime access URL presentation', () => {
  test('only HTTP and HTTPS URLs are clickable', () => {
    expect(isRuntimeUrlClickable('http://runtime.example:31001')).toBeTrue()
    expect(isRuntimeUrlClickable('HTTPS://runtime.example/path')).toBeTrue()
    expect(isRuntimeUrlClickable('tcp://runtime.example:31002')).toBeFalse()
    expect(isRuntimeUrlClickable('udp://runtime.example:31003')).toBeFalse()
    expect(isRuntimeUrlClickable('ssh://user@runtime.example:31004')).toBeFalse()
    expect(isRuntimeUrlClickable('javascript:alert(1)')).toBeFalse()
    expect(isRuntimeUrlClickable('not a URL')).toBeFalse()
    expect(isRuntimeUrlClickable('nc runtime.example 31005')).toBeFalse()
  })

  test('all runtime URL surfaces use the safe shared renderer', async () => {
    const paths = [
      '../app/features/challenges/RuntimeCard.vue',
      '../app/features/challenges/panels/AwdPanel.vue',
      '../app/features/challenges/panels/KohPanel.vue',
      '../app/pages/admin/competitions/[id]/runtimes.vue',
    ]

    for (const path of paths) {
      const source = await sourceFile(new URL(path, import.meta.url)).text()
      expect(source).toContain("<component :is=\"RuntimeAccessUrl\"")
    }

    const renderer = await sourceFile(
      new URL('../app/components/views/challenges/RuntimeAccessUrlView.vue', import.meta.url),
    ).text()
    expect(renderer).toContain('v-if="entry.clickable"')

    const controller = await sourceFile(
      new URL('../app/features/challenges/useRuntimeAccessUrl.ts', import.meta.url),
    ).text()
    expect(controller).toContain('navigator.clipboard.writeText(address)')
    expect(controller).toContain('webSocketAddress')
  })
})
