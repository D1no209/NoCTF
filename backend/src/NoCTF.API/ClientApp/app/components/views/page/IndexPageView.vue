<script setup lang="ts">
import { toRefs } from 'vue'
import type { IndexPageViewState } from '~/features/routes/useIndexPage'

const viewProps = defineProps<{ state: IndexPageViewState }>()
const { ArrowRight, configuration, isLoggedIn, competitionsError, loadCompetitions, terminalInput, terminalOutput, executeTerminalCommand } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col">
    <!-- Hero:左对齐非对称,网格背景收敛于内容区;右列为真实数据状态面板 -->
    <section class="home-hero relative flex overflow-hidden">
      <div
        class="pointer-events-none absolute inset-0 bg-[linear-gradient(to_right,var(--border)_1px,transparent_1px),linear-gradient(to_bottom,var(--border)_1px,transparent_1px)] bg-[size:48px_48px] opacity-60 [mask-image:radial-gradient(ellipse_60%_80%_at_30%_40%,black,transparent)]"
      />
      <div class="pointer-events-none absolute -top-40 -left-24 size-[32rem] rounded-full bg-primary/10 blur-3xl" />
      <div data-slot="home-signal-field" aria-hidden="true">
        <span data-slot="home-signal-base" />
        <span data-slot="home-signal-flow" class="noctf-motion-home-signal-flow" />
        <span data-slot="home-signal-flow" data-secondary class="noctf-motion-home-signal-flow" />
      </div>

      <div class="home-hero-content relative z-10 mx-auto grid w-full items-center gap-12 px-6 py-12 lg:grid-cols-[minmax(0,1.4fr)_minmax(320px,1fr)]">
        <div class="flex max-w-2xl flex-col items-start gap-6">
          <p class="font-mono text-sm text-primary md:text-base">
            {{ $t('common.label.welcome') }} {{ configuration?.name ?? $t('common.label.noctf') }}<span class="animate-blink">_</span>
          </p>
          <h1 class="text-display text-4xl md:text-6xl">
            {{ configuration?.name ?? $t('common.label.noctf') }}
          </h1>
          <p v-if="configuration?.description" class="max-w-xl text-lg text-muted-foreground">
            {{ configuration.description }}
          </p>
          <div class="mt-2 flex flex-wrap items-center gap-3">
            <Button size="lg" as-child>
              <NuxtLink to="/competitions"> {{ $t('common.label.browseCompetitions') }} <ArrowRight data-icon="inline-end" />
              </NuxtLink>
            </Button>
            <Button v-if="!isLoggedIn" size="lg" variant="outline" as-child>
              <NuxtLink to="/auth/register">{{ $t('common.label.registerNow') }}</NuxtLink>
            </Button>
          </div>
        </div>

        <PseudoTerminal
          v-model="terminalInput"
          :prompt="$t('terminal.prompt')"
          :input-label="$t('terminal.inputLabel')"
          :lines="terminalOutput"
          @submit="executeTerminalCommand"
        />
        <Alert v-if="competitionsError" variant="destructive">
          <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
            <span>{{ $message(competitionsError) }}</span>
            <Button type="button" size="sm" variant="outline" @click="loadCompetitions">{{ $t('common.label.reload') }}</Button>
          </AlertDescription>
        </Alert>
      </div>
    </section>

  </div>
</template>
