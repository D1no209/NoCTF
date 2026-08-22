<script setup lang="ts">
import { ArrowRight, Crosshair, Flag, Mountain, Swords } from '@lucide/vue'
import { cn } from '@/lib/utils'
import { listCompetitionsEndpoint } from '~/api'
import type { NoCtfapiEndpointsCompetitionsCompetitionResponse } from '~/api'

const { configuration } = usePlatform()
const { isLoggedIn } = useAuth()

const items = ref<NoCtfapiEndpointsCompetitionsCompetitionResponse[]>([])

onMounted(async () => {
  const { data } = await listCompetitionsEndpoint()
  items.value = data?.items ?? []
})

const activeStatuses: string[] = [
  'Running',
  'Paused',
  'Published',
  'Visible',
]

const recent = computed(() =>
  items.value
    .filter((c) => activeStatuses.includes(String(c.status)))
    .sort((a, b) => (a.endTime ?? '').localeCompare(b.endTime ?? ''))
    .slice(0, 6),
)

const liveCount = computed(
  () => items.value.filter((c) => c.status === 'Running').length,
)

const upcomingCount = computed(
  () =>
    items.value.filter((c) =>
      c.status === 'Published' || c.status === 'Visible',
    ).length,
)

const modes = [
  { key: 'Ctf', icon: Flag, title: 'CTF 解题赛', description: 'Web、Pwn、Crypto、Reverse 多方向题目,解题夺旗累计积分', featured: true },
  { key: 'Awd', icon: Swords, title: 'AWD 攻防赛', description: '攻防一体的实时对抗,漏洞利用与服务防御双重考验', featured: false },
  { key: 'Awdp', icon: Crosshair, title: 'AWDP 攻防增强', description: '在 AWD 之上引入修复环节,攻击 Break 与补丁 Fix 分开计分', featured: false },
  { key: 'Koh', icon: Mountain, title: 'KoH 占山为王', description: '持续占领目标,随时间累积积分,考验持久控制力', featured: true },
]
</script>

<template>
  <div class="flex flex-col">
    <!-- Hero:左对齐非对称,网格背景收敛于内容区;右列为真实数据状态面板 -->
    <section class="relative overflow-hidden border-b">
      <div
        class="pointer-events-none absolute inset-0 bg-[linear-gradient(to_right,var(--border)_1px,transparent_1px),linear-gradient(to_bottom,var(--border)_1px,transparent_1px)] bg-[size:48px_48px] opacity-60 [mask-image:radial-gradient(ellipse_60%_80%_at_30%_40%,black,transparent)]"
      />
      <div class="pointer-events-none absolute -top-40 -left-24 size-[32rem] rounded-full bg-primary/10 blur-3xl" />

      <div class="relative mx-auto grid w-full max-w-7xl items-center gap-12 px-4 py-20 md:px-6 md:py-28 lg:grid-cols-[1fr_360px]">
        <div class="flex max-w-2xl flex-col items-start gap-6">
          <p class="font-mono text-sm text-primary md:text-base">
            &gt; Welcome to {{ configuration?.name ?? 'NoCTF' }}<span class="animate-blink">_</span>
          </p>
          <h1 class="text-display text-4xl md:text-6xl">
            {{ configuration?.name ?? 'NoCTF' }}
          </h1>
          <p v-if="configuration?.description" class="max-w-xl text-lg text-muted-foreground">
            {{ configuration.description }}
          </p>
          <div class="mt-2 flex flex-wrap items-center gap-3">
            <Button size="lg" as-child>
              <NuxtLink to="/competitions"> {{ $t('浏览竞赛') }} <ArrowRight data-icon="inline-end" />
              </NuxtLink>
            </Button>
            <Button v-if="!isLoggedIn" size="lg" variant="outline" as-child>
              <NuxtLink to="/auth/register">{{ $t('立即注册') }}</NuxtLink>
            </Button>
          </div>
        </div>

        <div class="hidden lg:block">
          <div class="rounded-xl border bg-card p-5 font-mono text-sm">
            <p class="text-muted-foreground">$ noctf status</p>
            <dl class="mt-4 flex flex-col gap-3">
              <div class="flex items-center justify-between gap-4">
                <dt class="text-muted-foreground">modes</dt>
                <dd>ctf awd awdp koh</dd>
              </div>
              <div class="flex items-center justify-between gap-4">
                <dt class="text-muted-foreground">live</dt>
                <dd class="text-primary">{{ $t('{count} 场进行中', { count: liveCount }) }}</dd>
              </div>
              <div class="flex items-center justify-between gap-4">
                <dt class="text-muted-foreground">upcoming</dt>
                <dd>{{ $t('{count} 场即将开始', { count: upcomingCount }) }}</dd>
              </div>
            </dl>
          </div>
        </div>
      </div>
    </section>

    <!-- 近期竞赛 -->
    <section class="mx-auto w-full max-w-7xl px-4 py-14 md:px-6">
      <div class="mb-6 flex items-end justify-between gap-4">
        <div>
          <h2 class="text-2xl font-semibold">{{ $t('近期竞赛') }}</h2>
          <p class="mt-1 text-sm text-muted-foreground">{{ $t('正在进行与即将开始的比赛') }}</p>
        </div>
        <Button variant="ghost" as-child>
          <NuxtLink to="/competitions"> {{ $t('查看全部') }} <ArrowRight data-icon="inline-end" />
          </NuxtLink>
        </Button>
      </div>
      <div v-if="recent.length" class="grid gap-5 md:grid-cols-2 lg:grid-cols-3">
        <CompetitionCard v-for="c in recent" :key="c.id" :competition="c" />
      </div>
      <Empty v-else class="border border-dashed py-12">
        <EmptyHeader>
          <EmptyTitle>{{ $t('暂无进行中的竞赛') }}</EmptyTitle>
          <EmptyDescription>{{ $t('新竞赛发布后会在「动态」中通知,请稍后再来') }}</EmptyDescription>
        </EmptyHeader>
      </Empty>
    </section>

    <!-- 游戏模式:4 格非对称 bento,特色格带水印图标与品牌 tint -->
    <section class="border-t bg-muted/40">
      <div class="mx-auto w-full max-w-7xl px-4 py-14 md:px-6">
        <div class="mb-8">
          <h2 class="text-2xl font-semibold">{{ $t('四种游戏模式') }}</h2>
          <p class="mt-1 text-sm text-muted-foreground">{{ $t('从经典解题到实时攻防,一个平台全部覆盖') }}</p>
        </div>
        <div class="grid gap-5 sm:grid-cols-2 lg:grid-cols-6">
          <Card
            v-for="mode in modes"
            :key="mode.key"
            :class="cn(
              'relative overflow-hidden transition-all duration-300 hover:-translate-y-1 hover:border-primary/50',
              mode.featured ? 'lg:col-span-4 bg-gradient-to-br from-primary/8 to-transparent' : 'lg:col-span-2',
            )"
          >
            <component
              :is="mode.icon"
              v-if="mode.featured"
              class="pointer-events-none absolute -right-4 -bottom-6 size-36 text-primary/10"
            />
            <CardHeader>
              <span class="mb-2 flex size-10 items-center justify-center rounded-lg bg-primary/10 text-primary">
                <component :is="mode.icon" class="size-5" />
              </span>
              <CardTitle class="text-base">{{ $t(mode.title) }}</CardTitle>
              <CardDescription class="max-w-md leading-relaxed">{{ $t(mode.description) }}</CardDescription>
            </CardHeader>
          </Card>
        </div>
      </div>
    </section>
  </div>
</template>
