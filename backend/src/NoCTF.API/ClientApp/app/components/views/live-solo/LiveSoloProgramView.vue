<script setup lang="ts">
import { toRefs } from 'vue'
import type { LiveSoloProgramViewState } from '~/features/live-solo/useLiveSoloProgram'
const props = defineProps<{ state: LiveSoloProgramViewState }>()
const { program, state, source, stateKey, clock, error, load, fragment, failed, requestHeaders } = toRefs(props.state)
</script>
<template>
  <div class="mx-auto flex h-full w-full max-w-7xl min-h-0 flex-col gap-4 px-4 py-4" data-contained-workspace-page>
    <header class="flex flex-wrap items-center gap-3"><h1 class="text-display text-xl">{{ $t('liveSolo.program.title') }}</h1><Badge v-if="program">{{ $t('liveSolo.program.delay', { seconds: program.delaySeconds ?? 60 }) }}</Badge><Button class="ml-auto" variant="ghost" @click="load">{{ $t('common.label.refresh') }}</Button></header>
    <Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert>
    <Card class="min-h-0 flex-1"><ScrollSurface axis="y" class="h-full" :aria-label="$t('liveSolo.program.title')"><CardContent class="flex flex-col gap-5 py-5">
      <HlsVideoPlayer :key="program?.programCaptureId" :source="source" :request-headers="requestHeaders" :label="$t('liveSolo.program.title')" :play-label="$t('liveSolo.program.play')" :waiting-label="$t('liveSolo.program.waiting')" :error-label="$t('liveSolo.program.failed')" :retry-label="$t('common.label.retry')" @fragment="fragment" @failed="failed" @retry="load" />
      <template v-if="state"><div class="grid grid-cols-[1fr_auto_1fr] items-center gap-3 text-center"><h2 class="min-w-0 break-words text-xl font-semibold">{{ state.leftTeamName }}</h2><span class="font-mono text-4xl font-bold tabular-nums text-primary">{{ state.leftWins ?? 0 }} : {{ state.rightWins ?? 0 }}</span><h2 class="min-w-0 break-words text-xl font-semibold">{{ state.rightTeamName }}</h2></div><div class="flex flex-wrap items-center justify-center gap-3"><Badge>{{ $t(stateKey) }}</Badge><span v-if="state.roundNumber">{{ $t('liveSolo.round', { number: state.roundNumber }) }}</span><span class="font-mono tabular-nums">{{ clock }}</span><span class="text-sm text-muted-foreground">{{ $t('liveSolo.program.asOf', { time: formatDateTime(state.asOf) }) }}</span></div><div class="flex flex-wrap justify-center gap-2"><Badge v-for="question in state.questions" :key="question.id" variant="secondary">{{ question.title }}</Badge></div></template>
      <p v-else class="text-center text-sm text-muted-foreground">{{ $t('liveSolo.program.stateWaiting') }}</p>
    </CardContent></ScrollSurface></Card>
  </div>
</template>
