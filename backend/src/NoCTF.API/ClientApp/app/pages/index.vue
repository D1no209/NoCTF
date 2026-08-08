<script setup lang="ts">
import { ArrowRight, Crosshair, Flag, Mountain, Swords } from '@lucide/vue'
import { listCompetitionsEndpoint } from '~/api'
import type { NoCtfapiEndpointsCompetitionsCompetitionResponse } from '~/api'

const { configuration } = usePlatform()

const items = ref<NoCtfapiEndpointsCompetitionsCompetitionResponse[]>([])

onMounted(async () => {
  const { data } = await listCompetitionsEndpoint()
  items.value = data?.items ?? []
})

const activeStatuses: string[] = [
  CompetitionStatus.Running,
  CompetitionStatus.Paused,
  CompetitionStatus.Published,
  CompetitionStatus.Visible,
]

const recent = computed(() =>
  items.value
    .filter((c) => activeStatuses.includes(String(c.status)))
    .sort((a, b) => (a.endTime ?? '').localeCompare(b.endTime ?? ''))
    .slice(0, 6),
)

const modes = [
  { key: GameMode.Ctf, icon: Flag, title: 'CTF 解题赛', description: 'Web、Pwn、Crypto、Reverse 多方向题目,解题夺旗累计积分' },
  { key: GameMode.Awd, icon: Swords, title: 'AWD 攻防赛', description: '攻防一体的实时对抗,漏洞利用与服务防御双重考验' },
  { key: GameMode.Awdp, icon: Crosshair, title: 'AWDP 攻防增强', description: '在 AWD 之上引入修复环节,攻击 Break 与补丁 Fix 分开计分' },
  { key: GameMode.Koh, icon: Mountain, title: 'KoH 占山为王', description: '持续占领目标,随时间累积积分,考验持久控制力' },
]
</script>

<template>
  <div class="flex flex-col">
    <!-- Hero:网格 + 品牌光晕装饰,仅此区块使用 -->
    <section class="relative overflow-hidden border-b">
      <div
        class="pointer-events-none absolute inset-0 bg-[linear-gradient(to_right,var(--border)_1px,transparent_1px),linear-gradient(to_bottom,var(--border)_1px,transparent_1px)] bg-[size:48px_48px] opacity-60 [mask-image:radial-gradient(ellipse_70%_70%_at_50%_40%,black,transparent)]"
      />
      <div class="pointer-events-none absolute -top-32 left-1/2 size-[36rem] -translate-x-1/2 rounded-full bg-primary/15 blur-3xl" />

      <div class="relative mx-auto flex max-w-4xl flex-col items-center gap-6 px-4 py-24 text-center md:py-32">
        <p class="font-mono text-sm text-primary md:text-base">
          &gt; Welcome to {{ configuration?.name ?? 'NoCTF' }}<span class="animate-pulse">_</span>
        </p>
        <h1 class="text-4xl font-bold tracking-tight md:text-6xl">
          {{ configuration?.name ?? 'NoCTF' }}
        </h1>
        <p v-if="configuration?.description" class="max-w-2xl text-lg text-muted-foreground">
          {{ configuration.description }}
        </p>
        <div class="mt-2 flex flex-wrap items-center justify-center gap-3">
          <Button size="lg" as-child>
            <NuxtLink to="/competitions">
              浏览竞赛
              <ArrowRight data-icon="inline-end" />
            </NuxtLink>
          </Button>
          <Button size="lg" variant="outline" as-child>
            <NuxtLink to="/auth/register">立即注册</NuxtLink>
          </Button>
        </div>
      </div>
    </section>

    <!-- 近期竞赛 -->
    <section class="mx-auto w-full max-w-7xl px-4 py-14 md:px-6">
      <div class="mb-6 flex items-end justify-between gap-4">
        <div>
          <h2 class="text-2xl font-semibold">近期竞赛</h2>
          <p class="mt-1 text-sm text-muted-foreground">正在进行与即将开始的比赛</p>
        </div>
        <Button variant="ghost" as-child>
          <NuxtLink to="/competitions">
            查看全部
            <ArrowRight data-icon="inline-end" />
          </NuxtLink>
        </Button>
      </div>
      <div v-if="recent.length" class="grid gap-5 md:grid-cols-2 lg:grid-cols-3">
        <CompetitionCard v-for="c in recent" :key="c.id" :competition="c" />
      </div>
      <Empty v-else class="border border-dashed py-12">
        <EmptyHeader>
          <EmptyTitle>暂无进行中的竞赛</EmptyTitle>
          <EmptyDescription>新竞赛发布后会在「动态」中通知,请稍后再来</EmptyDescription>
        </EmptyHeader>
      </Empty>
    </section>

    <!-- 游戏模式 -->
    <section class="border-t bg-muted/40">
      <div class="mx-auto w-full max-w-7xl px-4 py-14 md:px-6">
        <div class="mb-8">
          <h2 class="text-2xl font-semibold">四种游戏模式</h2>
          <p class="mt-1 text-sm text-muted-foreground">从经典解题到实时攻防,一个平台全部覆盖</p>
        </div>
        <div class="grid gap-5 sm:grid-cols-2 lg:grid-cols-4">
          <Card v-for="mode in modes" :key="mode.key" class="transition-all duration-300 hover:-translate-y-1 hover:border-primary/50 hover:shadow-lg">
            <CardHeader>
              <span class="mb-2 flex size-10 items-center justify-center rounded-lg bg-primary/10 text-primary">
                <component :is="mode.icon" class="size-5" />
              </span>
              <CardTitle class="text-base">{{ mode.title }}</CardTitle>
              <CardDescription class="leading-relaxed">{{ mode.description }}</CardDescription>
            </CardHeader>
          </Card>
        </div>
      </div>
    </section>
  </div>
</template>
