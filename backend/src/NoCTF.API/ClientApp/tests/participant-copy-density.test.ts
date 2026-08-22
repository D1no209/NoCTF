import { describe, expect, test } from 'bun:test'

describe('participant copy density', () => {
  test('keeps challenge actions concise without repeating their behavior', async () => {
    const [detail, runtime, awd, awdp, koh] = await Promise.all([
      Bun.file(new URL('../app/components/challenges/CompetitionChallengeDetail.vue', import.meta.url)).text(),
      Bun.file(new URL('../app/components/challenges/RuntimeCard.vue', import.meta.url)).text(),
      Bun.file(new URL('../app/components/challenges/panels/AwdPanel.vue', import.meta.url)).text(),
      Bun.file(new URL('../app/components/challenges/panels/AwdpPanel.vue', import.meta.url)).text(),
      Bun.file(new URL('../app/components/challenges/panels/KohPanel.vue', import.meta.url)).text(),
    ])

    expect(detail).not.toContain("$t('题面')")
    expect(detail).not.toContain("$t('本题没有附件。')")
    expect(runtime).not.toContain('环境尚未启动,点击')
    expect(awd).not.toContain('AWD 模式:')
    expect(awdp).not.toContain('提交当前攻击实例中取得的单个动态 Flag')
    expect(koh).not.toContain('KoH 模式:')
  })

  test('does not explain invitation-token implementation beside the action', async () => {
    const teamPage = await Bun.file(
      new URL('../app/pages/competitions/[id]/my/team.vue', import.meta.url),
    ).text()

    expect(teamPage).not.toContain('邀请码在创建队伍时生成')
    expect(teamPage).toContain("$t('轮换邀请码')")
  })

  test('uses one continuous challenge work surface instead of nested floating cards', async () => {
    const [detail, runtime, flag, fix, awd, awdp, koh] = await Promise.all([
      Bun.file(new URL('../app/components/challenges/CompetitionChallengeDetail.vue', import.meta.url)).text(),
      Bun.file(new URL('../app/components/challenges/RuntimeCard.vue', import.meta.url)).text(),
      Bun.file(new URL('../app/components/challenges/FlagSubmit.vue', import.meta.url)).text(),
      Bun.file(new URL('../app/components/challenges/FixSubmit.vue', import.meta.url)).text(),
      Bun.file(new URL('../app/components/challenges/panels/AwdPanel.vue', import.meta.url)).text(),
      Bun.file(new URL('../app/components/challenges/panels/AwdpPanel.vue', import.meta.url)).text(),
      Bun.file(new URL('../app/components/challenges/panels/KohPanel.vue', import.meta.url)).text(),
    ])

    for (const source of [detail, runtime, flag, fix, awd, awdp, koh]) {
      expect(source).not.toContain('<Card')
    }
    expect(detail).toContain('class="border-b py-5"')
    expect(awdp).toContain('xl:border-l')
    expect(koh).toContain('md:divide-x')
  })
})
