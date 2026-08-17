import { describe, expect, test } from 'bun:test'

describe('AWDP participant panel', () => {
  test('separates team attack runtime from one-shot fix verification', async () => {
    const source = await Bun.file(
      new URL('../app/components/challenges/panels/AwdpPanel.vue', import.meta.url),
    ).text()

    expect(source).toContain('攻击轨 · Break')
    expect(source).toContain('防御轨 · Fix')
    expect(source).toContain('<RuntimeCard')
    expect(source).toContain('controls="full"')
    expect(source).toContain('<FlagSubmit')
    expect(source).toContain('<FixSubmit')
    expect(source).toContain('创建本队独立攻击实例')
    expect(source).toContain('只执行一次 Checker')
  })

  test('does not expose AWD service-state or hardening language', async () => {
    const source = await Bun.file(
      new URL('../app/components/challenges/panels/AwdpPanel.vue', import.meta.url),
    ).text()

    expect(source).not.toContain('硬化')
    expect(source).not.toContain('Hardening')
    expect(source).not.toContain('服务 Up')
    expect(source).not.toContain('服务 Down')
    expect(source).not.toContain('轮换')
    expect(source).not.toContain('AwdRotation')
  })
})
