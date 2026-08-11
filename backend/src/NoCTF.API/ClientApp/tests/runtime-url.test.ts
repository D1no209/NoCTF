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
  })

  test('all runtime URL surfaces use the safe shared renderer', async () => {
    const paths = [
      '../app/components/challenges/RuntimeCard.vue',
      '../app/components/challenges/panels/AwdPanel.vue',
      '../app/components/challenges/panels/KohPanel.vue',
      '../app/pages/admin/competitions/[id]/runtimes.vue',
    ]

    for (const path of paths) {
      const source = await Bun.file(new URL(path, import.meta.url)).text()
      expect(source).toContain('<RuntimeAccessUrl')
    }

    const renderer = await Bun.file(
      new URL('../app/components/challenges/RuntimeAccessUrl.vue', import.meta.url),
    ).text()
    expect(renderer).toContain('v-if="clickable"')
    expect(renderer).toContain('navigator.clipboard.writeText(props.url)')
  })
})
