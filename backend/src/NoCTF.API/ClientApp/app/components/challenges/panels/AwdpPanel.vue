<script setup lang="ts">
import { listSubmissionsEndpoint } from '~/api'
import type {
  NoCtfapiEndpointsChallengesChallengeResponse,
  NoCtfapiEndpointsCompetitionsCompetitionResponse,
} from '~/api'

const props = defineProps<{
  competition: NoCtfapiEndpointsCompetitionsCompetitionResponse
  challenge: NoCtfapiEndpointsChallengesChallengeResponse
}>()

const { isLoggedIn } = useAuth()

// 是否已有评测正确的 Break 提交(用于 RequireBreakBeforeFix 场景的入口提示)
const breakSucceeded = ref(false)
const breakChecked = ref(false)

async function checkBreak() {
  if (!isLoggedIn.value) return
  const { data, error } = await listSubmissionsEndpoint({
    path: { competitionId: props.competition.id! },
    query: { limit: 200 },
  })
  breakChecked.value = true
  if (error || !data) return
  breakSucceeded.value = (data.items ?? []).some(
    (s) =>
      s.competitionChallengeId === props.challenge.id
      && s.kind === SubmissionKind.Break
      && s.result === ScoringResult.Correct,
  )
}

onMounted(checkBreak)
</script>

<template>
  <div class="flex flex-col gap-6">
    <Alert>
      <AlertDescription>
        AWDP 模式:Break 轨提交从对手靶机获取的 flag;Fix 轨上传修复归档修补己方服务。
      </AlertDescription>
    </Alert>

    <FlagSubmit
      :competition-id="competition.id!"
      :competition-challenge-id="challenge.id!"
      title="Break:提交 Flag"
      description="攻击对手靶机获取 flag 后在此提交"
      @evaluated="checkBreak"
    />

    <FixSubmit
      :competition-id="competition.id!"
      :competition-challenge-id="challenge.id!"
      :disabled="isLoggedIn && breakChecked && !breakSucceeded"
      :disabled-reason="'本场可能要求先 Break 成功才能提交 Fix:你目前还没有评测正确的 Break 提交。若赛事无此限制,可直接联系主办方确认;提交被后端拒绝时会返回具体原因。'"
      @evaluated="checkBreak"
    />
  </div>
</template>
