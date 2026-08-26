<script setup lang="ts">
import { listRuntimeTargetsEndpoint } from '~/api'
import type {
  NoCtfapiEndpointsChallengesChallengeResponse,
  NoCtfapiEndpointsCompetitionsCompetitionResponse,
  NoCtfapiEndpointsRuntimeRuntimeTargetResponse,
} from '~/api'

const props = defineProps<{
  competition: NoCtfapiEndpointsCompetitionsCompetitionResponse
  challenge: NoCtfapiEndpointsChallengesChallengeResponse
}>()

const targets = ref<NoCtfapiEndpointsRuntimeRuntimeTargetResponse[]>([])
const targetsError = ref<string | null>(null)
const targetsLoaded = ref(false)

onMounted(async () => {
  const { data, error } = await listRuntimeTargetsEndpoint({
    path: { competitionId: props.competition.id!, competitionChallengeId: props.challenge.id! },
  })
  targetsLoaded.value = true
  if (error || !data) {
    targetsError.value = parseApiError(error, translate("加载攻击目标失败")).message
    return
  }
  targets.value = data.items ?? []
})
</script>

<template>
  <div class="flex flex-col divide-y">
    <RuntimeCard
      class="pb-5"
      :competition-id="competition.id!"
      :competition-challenge-id="challenge.id!"
      controls="reset-only"
    />

    <section class="py-5" aria-labelledby="awd-targets-title">
      <h3 id="awd-targets-title" class="mb-4 text-sm font-semibold">{{ $t('攻击目标') }}</h3>
        <Skeleton v-if="!targetsLoaded" class="h-16 w-full" />
        <Alert v-else-if="targetsError">
          <AlertDescription>{{ targetsError }}</AlertDescription>
        </Alert>
        <Empty v-else-if="!targets.length" class="border py-8">
          <EmptyHeader>
            <EmptyTitle>{{ $t('暂无可攻击的目标') }}</EmptyTitle>
          </EmptyHeader>
        </Empty>
        <Table v-else>
          <TableHeader>
            <TableRow>
              <TableHead>{{ $t('队伍') }}</TableHead>
              <TableHead>{{ $t('靶机地址') }}</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-for="target in targets" :key="target.teamId">
              <TableCell class="font-medium">{{ target.teamName }}</TableCell>
              <TableCell>
                <div class="flex flex-col gap-1">
                  <RuntimeAccessUrl
                    v-for="url in target.urls ?? []"
                    :key="url"
                    :url="url"
                  />
                </div>
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
    </section>

    <FlagSubmit
      class="pt-5"
      :competition-id="competition.id!"
      :competition-challenge-id="challenge.id!"
      multiple
      :title="$t('批量提交 Flag')"
      :maximum-attempts="challenge.maximumFlagAttempts"
      :remaining-attempts="challenge.remainingFlagAttempts"
    />
  </div>
</template>
