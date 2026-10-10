<script setup lang="ts">
import { toRefs } from 'vue'
import type { LiveSoloProgramViewState } from '~/features/live-solo/useLiveSoloProgram'
const props = defineProps<{ state: LiveSoloProgramViewState }>()
const { program, state, source, result, resultVoided, stateKey, clock, error, load, fragment, failed, requestHeaders, admitted, entering, enter, leave } = toRefs(props.state)
</script>
<template>
  <div class="mx-auto flex w-full max-w-7xl min-w-0 flex-col gap-6 px-4 pb-8 pt-4 md:px-8">
    <header class="flex flex-wrap items-center gap-3"><h1 class="text-display text-xl">{{ $t('liveSolo.program.title') }}</h1><Badge v-if="program">{{ $t('liveSolo.program.delay', { seconds: program.delaySeconds ?? 60 }) }}</Badge><Button v-if="admitted" class="ml-auto" variant="ghost" @click="leave">{{ $t('liveSolo.program.leave') }}</Button></header>
    <Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert>
    <Card class="min-w-0"><CardContent class="flex min-w-0 flex-col gap-6">
      <template v-if="state"><div class="grid grid-cols-[minmax(0,1fr)_auto_minmax(0,1fr)] items-center gap-3 text-center"><h2 class="min-w-0 break-words text-base font-semibold md:text-xl">{{ state.leftTeamName }}</h2><span class="font-mono text-2xl font-bold md:text-4xl tabular-nums text-primary">{{ state.leftWins ?? 0 }} : {{ state.rightWins ?? 0 }}</span><h2 class="min-w-0 break-words text-base font-semibold md:text-xl">{{ state.rightTeamName }}</h2></div><div class="flex flex-wrap items-center justify-center gap-3"><Badge>{{ $t(stateKey) }}</Badge><span v-if="state.roundNumber">{{ $t('liveSolo.round', { number: state.roundNumber }) }}</span><span class="font-mono tabular-nums">{{ clock }}</span><span class="text-sm text-muted-foreground">{{ $t('liveSolo.program.asOf', { time: formatDateTime(state.asOf) }) }}</span></div><div class="flex flex-wrap justify-center gap-2"><Badge v-for="question in state.questions" :key="question.id" variant="secondary">{{ question.title }}</Badge></div></template>
      <p v-else-if="!result" class="text-center text-sm text-muted-foreground">{{ $t(program?.ended && !source ? 'liveSolo.program.videoWindowEnded' : 'liveSolo.program.stateWaiting') }}</p>
      <HlsVideoPlayer v-if="admitted && (!program?.ended || source)" :key="program?.programCaptureId" :source="source" :request-headers="requestHeaders" :label="$t('liveSolo.program.title')" :play-label="$t('liveSolo.program.play')" :waiting-label="$t(program?.ended && !source ? 'liveSolo.program.videoWindowEnded' : 'liveSolo.program.waiting')" :error-label="$t('liveSolo.program.failed')" :retry-label="$t('common.label.retry')" @fragment="fragment" @failed="failed" @retry="load" />
      <Empty v-else-if="admitted"><EmptyHeader><EmptyTitle>{{ $t('liveSolo.program.videoWindowEnded') }}</EmptyTitle></EmptyHeader></Empty>
      <Empty v-else><EmptyHeader><EmptyTitle>{{ $t('liveSolo.program.entry') }}</EmptyTitle></EmptyHeader><Button :disabled="entering" @click="enter"><Spinner v-if="entering" data-icon="inline-start" />{{ $t('liveSolo.program.enter') }}</Button></Empty>
      <section v-if="result" class="flex flex-col gap-3 pt-5" :aria-label="$t('liveSolo.program.resultTitle')"><h2 class="text-display text-lg">{{ $t('liveSolo.program.resultTitle') }}</h2><div class="flex flex-wrap items-center gap-3"><span v-if="!resultVoided" class="font-mono text-2xl font-bold tabular-nums">{{ result.leftWins }} : {{ result.rightWins }}</span><Badge v-if="result.winnerTeamName">{{ $t('liveSolo.program.winner', { team: result.winnerTeamName }) }}</Badge><Badge v-else variant="secondary">{{ $t(resultVoided ? 'liveSolo.program.voided' : 'liveSolo.program.noWinner') }}</Badge></div><p class="text-sm text-muted-foreground">{{ $t('liveSolo.program.resultAsOf', { time: formatDateTime(result.asOf) }) }}</p></section>
    </CardContent></Card>
  </div>
</template>
