<script setup lang="ts">
import { toRefs } from 'vue'
import type { AwdpPanelViewState } from '~/features/challenges/panels/useAwdpPanel'

const viewProps = defineProps<{ state: AwdpPanelViewState }>()
const { ShieldCheck, emit, state, loading, stateError, defenseOutcome, statePollingTimedOut, refreshAndPoll, handleBreakEvaluation, handleFixAccepted, FixSubmit, FlagSubmit, RuntimeCard, setAttackRuntimeCardRef, competition, challenge } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-6">
    <Alert v-if="statePollingTimedOut" variant="destructive">
      <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
        <span>{{ $t('ui.automaticAwdpStatusUpdatesStoppedRetryManually') }}</span>
        <Button type="button" size="sm" variant="outline" @click="refreshAndPoll">{{ $t('ui.reload') }}</Button>
      </AlertDescription>
    </Alert>
    <Alert v-else-if="stateError" variant="destructive">
      <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
        <span>{{ $message(stateError) }}</span>
        <Button type="button" size="sm" variant="outline" @click="refreshAndPoll">{{ $t('ui.reload') }}</Button>
      </AlertDescription>
    </Alert>
    <div v-if="loading" class="grid gap-6 lg:grid-cols-2">
      <Skeleton class="h-80 w-full" />
      <Skeleton class="h-80 w-full" />
    </div>

    <div v-else class="grid items-start gap-6 xl:grid-cols-2 xl:gap-0">
      <section class="flex min-w-0 flex-col gap-5 xl:pr-6" aria-labelledby="awdp-attack-title">
        <header class="flex flex-wrap items-center justify-between gap-3 border-b pb-4">
          <div class="flex flex-wrap items-center gap-2">
            <h2 id="awdp-attack-title" class="text-lg font-semibold">{{ $t('ui.attackTargetBreakEnvironment') }}</h2>
            <Badge v-if="state?.breakActivation" variant="default">
              {{ $t('ui.activeSinceRound', { round: state.breakActivation.effectiveRound ?? '-' }) }}
            </Badge>
          </div>
        </header>

        <component :is="RuntimeCard"
          :ref="setAttackRuntimeCardRef"
          class="border-b pb-5"
          :competition-id="competition.id!"
          :competition-challenge-id="challenge.id!"
          :controls="state?.breakActivation ? 'readonly' : 'full'"
        />

        <component :is="FlagSubmit"
          class="border-t pt-5"
          :competition-id="competition.id!"
          :competition-challenge-id="challenge.id!"
          :title="state?.breakActivation ? $t('ui.checkFlag') : $t('ui.submitFlag')"
          :read-only-judgement="!!state?.breakActivation"
          :maximum-attempts="challenge.maximumFlagAttempts"
          :remaining-attempts="challenge.remainingFlagAttempts"
          @evaluated="handleBreakEvaluation"
          @submitted="emit('submitted')"
        />
      </section>

      <section class="flex min-w-0 flex-col gap-5 border-t pt-6 xl:border-l xl:border-t-0 xl:pl-6 xl:pt-0" aria-labelledby="awdp-defense-title">
        <header class="flex flex-wrap items-center gap-3 border-b pb-4">
          <div class="flex flex-wrap items-center gap-2">
            <h2 id="awdp-defense-title" class="text-lg font-semibold">{{ $t('ui.defenseFix') }}</h2>
            <Badge v-if="state?.fixActivation" variant="default">
              {{ $t('ui.activeSinceRound', { round: state.fixActivation.effectiveRound ?? '-' }) }}
            </Badge>
            <Badge
              v-if="state?.maximumFixAttempts !== null && state?.maximumFixAttempts !== undefined"
              variant="secondary"
              class="font-mono tabular-nums"
            >
              {{ $t('ui.submissionsRemaining', { count: state.remainingFixAttempts ?? 0 }) }}
            </Badge>
          </div>
        </header>

        <section v-if="state?.defense?.gameplayFactId" class="border-b pb-5" aria-labelledby="awdp-last-fix-title">
            <h3 id="awdp-last-fix-title" class="mb-3 text-sm font-semibold">{{ $t('ui.latestFixVerification') }}</h3>
            <div class="flex flex-wrap items-center justify-between gap-3 text-sm">
              <Badge v-if="state.defense.state" variant="outline">
                {{ gameplayFactStateLabel(state.defense.state) }}
              </Badge>
              <strong v-if="defenseOutcome" :class="state.defense.result === 'Correct' ? 'text-emerald-600 dark:text-emerald-400' : 'text-destructive'">
                {{ defenseOutcome }}
              </strong>
            </div>
        </section>

        <Alert v-if="state?.fixActivation" class="border-sky-500/40 bg-sky-500/5">
          <ShieldCheck class="text-sky-600 dark:text-sky-400" />
          <AlertTitle>{{ $t('ui.defenseLockedNoFurtherVerificationIsRequired') }}</AlertTitle>
        </Alert>

        <component :is="FixSubmit"
          v-else
          class="border-t pt-5"
          :competition-id="competition.id!"
          :competition-challenge-id="challenge.id!"
          :defense="state?.defense"
          @changed="refreshAndPoll"
          @accepted="handleFixAccepted"
        />
      </section>
    </div>

  </div>
</template>
