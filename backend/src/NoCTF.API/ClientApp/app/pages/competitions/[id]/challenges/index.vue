<script setup lang="ts">
const route = useRoute()
const router = useRouter()
const competitionId = route.params.id as string

const selectedChallengeId = computed(() =>
  typeof route.query.challenge === 'string' ? route.query.challenge : null,
)

async function selectChallenge(challengeId: string): Promise<void> {
  if (selectedChallengeId.value === challengeId) return
  await router.replace({
    path: `/competitions/${competitionId}/challenges`,
    query: { ...route.query, challenge: challengeId },
  })
}
</script>

<template>
  <CompetitionParticipantWorkspace
    :competition-id="competitionId"
    :selected-challenge-id="selectedChallengeId"
    challenge-selection-mode="inline"
    @select-challenge="selectChallenge"
  >
    <CompetitionChallengeDetail
      v-if="selectedChallengeId"
      :competition-id="competitionId"
      :competition-challenge-id="selectedChallengeId"
    />
    <Empty v-else class="h-full min-h-80 border-0">
      <EmptyHeader>
        <EmptyTitle>{{ $t('选择题目查看详情') }}</EmptyTitle>
      </EmptyHeader>
    </Empty>
  </CompetitionParticipantWorkspace>
</template>
