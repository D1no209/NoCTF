<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { toast } from 'vue-sonner'
import AttackLogFeed from '@/ui-v1/components/game/AttackLogFeed.vue'
import AwdBattlefieldCore from '@/ui-v1/components/game/AwdBattlefieldCore.vue'
import RoundTimer from '@/ui-v1/components/game/RoundTimer.vue'
import ServiceStatusGrid from '@/ui-v1/components/game/ServiceStatusGrid.vue'
import AwdFlagSubmissionCard from '@/ui-v1/components/awd/AwdFlagSubmissionCard.vue'
import AwdpPatchStatusList from '@/ui-v1/components/awdp/AwdpPatchStatusList.vue'
import AwdpPatchUploadCard from '@/ui-v1/components/awdp/AwdpPatchUploadCard.vue'
import { Card } from '@/ui-v1/components/ui/card'
import { useAwdDashboardPage } from '@/features/competitions/useAwdDashboardPage'

const props = withDefaults(defineProps<{ gameModeType?: string }>(), { gameModeType: 'Awd' })
const { t } = useI18n()

const {
  isAwdp,
  round,
  remainingSeconds,
  totalSeconds,
  services,
  teams,
  challenges,
  attackLogs,
  localAwarenessEvents,
  patchChallenge,
  patchFile,
  patchLoading,
  isDragOver,
  patchStatuses,
  submitPatch: submitPatchAction,
  selectedVictim,
  selectedChallenge,
  flagInput,
  flagLoading,
  submitFlag: submitFlagAction,
  enqueueAttackFailed,
} = useAwdDashboardPage(() => props.gameModeType)

async function submitPatch() {
  const outcome = await submitPatchAction()
  if (outcome.kind === 'ok') {
    toast.success(t('awd.patchSubmitted'), {
      description: outcome.submissionId ? t('awd.patchSubmissionId', { id: outcome.submissionId }) : undefined,
    })
  }
  else if (outcome.kind === 'failed') {
    toast.error(t('awd.patchUploadFailed'))
  }
}

async function submitFlag() {
  const outcome = await submitFlagAction()
  if (outcome.kind === 'correct') {
    toast.success(t('challenges.correctFlag'))
  }
  else if (outcome.kind === 'incorrect') {
    const message = outcome.message ?? t('challenges.incorrectFlag')
    enqueueAttackFailed(message, {
      currentTeamName: t('awd.currentTeam'),
      selectedTargetName: t('awd.selectedTarget'),
      selectedServiceName: t('awd.selectedService'),
    })
    toast.error(message)
  }
  else if (outcome.kind === 'failed') {
    toast.error(t('challenges.submissionFailed'))
  }
}
</script>

<template>
  <div class="mx-auto w-full max-w-[1700px] space-y-6 px-4 py-6 md:px-6 lg:px-8">
    <Card class="px-4 py-4">
      <RoundTimer
        :round="round"
        :remaining-seconds="remainingSeconds"
        :total-seconds="totalSeconds"
      />
    </Card>

    <AwdBattlefieldCore
      :round="round"
      :services="services"
      :attack-logs="attackLogs"
      :external-events="localAwarenessEvents"
    />

    <div class="grid w-full grid-cols-12 gap-6">
      <div class="col-span-12 space-y-4 lg:col-span-3">
        <h2 class="px-1">
          {{ t('awd.serviceStatus') }}
        </h2>
        <ServiceStatusGrid :services="services" />
      </div>

      <div class="col-span-12 space-y-6 lg:col-span-6">
        <AwdFlagSubmissionCard
          v-model:selected-victim="selectedVictim"
          v-model:selected-challenge="selectedChallenge"
          v-model:flag-input="flagInput"
          :teams="teams ?? []"
          :challenges="challenges ?? []"
          :loading="flagLoading"
          @submit="submitFlag"
        />

        <transition name="slide-up">
          <AwdpPatchUploadCard
            v-if="isAwdp"
            v-model:patch-challenge="patchChallenge"
            v-model:patch-file="patchFile"
            v-model:is-drag-over="isDragOver"
            :challenges="challenges ?? []"
            :patch-loading="patchLoading"
            @submit="submitPatch"
          />
        </transition>

        <transition name="fade">
          <AwdpPatchStatusList
            v-if="isAwdp"
            :patch-statuses="patchStatuses"
          />
        </transition>
      </div>

      <div class="col-span-12 space-y-4 lg:col-span-3">
        <h2 class="px-1">
          {{ t('awd.realtimeActivity') }}
        </h2>
        <AttackLogFeed :logs="attackLogs" />
      </div>
    </div>
  </div>
</template>

<style scoped>
.fade-enter-active, .fade-leave-active { transition: opacity 0.3s ease; }
.fade-enter-from, .fade-leave-to { opacity: 0; }

.slide-up-enter-active, .slide-up-leave-active { transition: all 0.4s cubic-bezier(0.16, 1, 0.3, 1); }
.slide-up-enter-from { opacity: 0; transform: translateY(20px); }
.slide-up-leave-to { opacity: 0; transform: translateY(-20px); }
</style>