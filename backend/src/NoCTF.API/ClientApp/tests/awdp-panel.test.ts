import { describe, expect, test } from 'bun:test'

describe('AWDP participant panel', () => {
  test('separates team attack runtime from one-shot fix verification', async () => {
    const source = await Bun.file(
      new URL('../app/components/challenges/panels/AwdpPanel.vue', import.meta.url),
    ).text()

    expect(source).toContain('攻击靶机 · Break 环境')
    expect(source).toContain('防御轨 · Fix')
    expect(source).toContain('<RuntimeCard')
    expect(source).toContain('controls="full"')
    expect(source).toContain('<FlagSubmit')
    expect(source).toContain('<FixSubmit')
    expect(source).toContain('创建本队独立攻击实例')
    expect(source).toContain('只执行一次 Checker')
  })

  test('models defense as an explicit one-shot target with one upload', async () => {
    const source = await Bun.file(
      new URL('../app/components/challenges/FixSubmit.vue', import.meta.url),
    ).text()

    expect(source).toContain('requestAwdpDefenseTargetEndpoint')
    expect(source).toContain('uploadPatchEndpoint')
    expect(source).toContain('AwaitingPatch')
    expect(source).toContain('申请防御环境')
    expect(source).toContain('上传本次 Fix 包')
    expect(source).toContain('正在应用补丁并执行一次性验证 Checker')
    expect(source).toContain('环境已回收')
    expect(source).not.toContain('submitFixEndpoint')
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

  test('labels the AWDP checker as a one-shot Fix verifier', async () => {
    const source = await Bun.file(
      new URL('../app/components/admin/DefinitionCheckerSection.vue', import.meta.url),
    ).text()
    const awdpBranch = source.slice(source.indexOf("v-else-if=\"mode === 'Awdp'\""))

    expect(awdpBranch).toContain('Fix 一次性验证 Checker')
    expect(awdpBranch).toContain('启用 Fix 一次性验证 Checker')
    expect(awdpBranch).not.toContain('周期性服务检查')
    expect(awdpBranch).not.toContain('服务健康检查')
  })
})
