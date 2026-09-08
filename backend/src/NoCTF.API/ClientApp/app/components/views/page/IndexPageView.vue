<script setup lang="ts">
import { toRefs } from 'vue'
import type { IndexPageViewState } from '~/features/routes/useIndexPage'

const viewProps = defineProps<{ state: IndexPageViewState }>()
const { ArrowRight, cn, configuration, isLoggedIn, items, competitionsLoading, competitionsError, loadCompetitions, recent, liveCount, upcomingCount, modes, CompetitionCard } = toRefs(viewProps.state)
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
            {{ $t('ui.welcomeTo') }} {{ configuration?.name ?? $t('ui.noctf') }}<span class="animate-blink">_</span>
          </p>
          <h1 class="text-display text-4xl md:text-6xl">
            {{ configuration?.name ?? $t('ui.noctf') }}
          </h1>
          <p v-if="configuration?.description" class="max-w-xl text-lg text-muted-foreground">
            {{ configuration.description }}
          </p>
          <div class="mt-2 flex flex-wrap items-center gap-3">
            <Button size="lg" as-child>
              <NuxtLink to="/competitions"> {{ $t('ui.browseCompetitions') }} <ArrowRight data-icon="inline-end" />
              </NuxtLink>
            </Button>
            <Button v-if="!isLoggedIn" size="lg" variant="outline" as-child>
              <NuxtLink to="/auth/register">{{ $t('ui.registerNow') }}</NuxtLink>
            </Button>
          </div>
        </div>

        <div class="hidden lg:block">
          <div class="rounded-xl border bg-card p-5 font-mono text-sm">
            <p class="text-muted-foreground">{{ $t('ui.noctfStatus') }}</p>
            <dl class="mt-4 flex flex-col gap-3">
              <div class="flex items-center justify-between gap-4">
                <dt class="text-muted-foreground">{{ $t('ui.modes') }}</dt>
                <dd>{{ $t('ui.ctfAwdAwdpKoh') }}</dd>
              </div>
              <div class="flex items-center justify-between gap-4">
                <dt class="text-muted-foreground">{{ $t('ui.live2') }}</dt>
                <dd class="text-primary">{{ $t('ui.running4', { count: liveCount }) }}</dd>
              </div>
              <div class="flex items-center justify-between gap-4">
                <dt class="text-muted-foreground">{{ $t('ui.upcoming3') }}</dt>
                <dd>{{ $t('ui.upcoming2', { count: upcomingCount }) }}</dd>
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
          <h2 class="text-2xl font-semibold">{{ $t('ui.recentCompetitions') }}</h2>
          <p class="mt-1 text-sm text-muted-foreground">{{ $t('ui.ongoingAndUpcomingMatches') }}</p>
        </div>
        <Button variant="ghost" as-child>
          <NuxtLink to="/competitions"> {{ $t('ui.viewAll') }} <ArrowRight data-icon="inline-end" />
          </NuxtLink>
        </Button>
      </div>
      <div v-if="competitionsLoading" class="grid gap-5 md:grid-cols-2 lg:grid-cols-3" :aria-label="$t('ui.loadingRecentCompetitions')">
        <Skeleton v-for="index in 3" :key="index" class="h-44 w-full" />
      </div>
      <Alert v-else-if="competitionsError" variant="destructive">
        <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
          <span>{{ $message(competitionsError) }}</span>
          <Button type="button" size="sm" variant="outline" @click="loadCompetitions">{{ $t('ui.reload') }}</Button>
        </AlertDescription>
      </Alert>
      <div v-else-if="recent.length" class="grid gap-5 md:grid-cols-2 lg:grid-cols-3">
        <component :is="CompetitionCard" v-for="c in recent" :key="c.id" :competition="c" />
      </div>
      <Empty v-else class="border border-dashed py-12">
        <EmptyHeader>
          <EmptyTitle>{{ $t('ui.thereAreNoOngoingContests') }}</EmptyTitle>
          <EmptyDescription>{{ $t('ui.newCompetitionsWillBeNotifiedInNewsAfterTheyAre') }}</EmptyDescription>
        </EmptyHeader>
      </Empty>
    </section>

    <!-- 游戏模式:4 格非对称 bento,特色格带水印图标与品牌 tint -->
    <section class="border-t bg-muted/40">
      <div class="mx-auto w-full max-w-7xl px-4 py-14 md:px-6">
        <div class="mb-8">
          <h2 class="text-2xl font-semibold">{{ $t('ui.fourGameModes') }}</h2>
          <p class="mt-1 text-sm text-muted-foreground">{{ $t('ui.fromClassicProblemSolvingToRealTimeAttackAndDefense') }}</p>
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
