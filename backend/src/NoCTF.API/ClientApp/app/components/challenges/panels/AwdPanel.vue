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
  <div class="flex flex-col gap-6">
    <Alert>
      <AlertDescription> {{ $t('AWD 模式:维护好自己的服务不被攻击,同时攻击其他队伍的靶机获取 flag 批量提交。') }} </AlertDescription>
    </Alert>

    <RuntimeCard
      :competition-id="competition.id!"
      :competition-challenge-id="challenge.id!"
      controls="reset-only"
    />

    <Card>
      <CardHeader>
        <CardTitle class="text-base">{{ $t('攻击目标') }}</CardTitle>
        <CardDescription>{{ $t('加固期结束后,这里会列出其他队伍的靶机地址') }}</CardDescription>
      </CardHeader>
      <CardContent>
        <Skeleton v-if="!targetsLoaded" class="h-16 w-full" />
        <Alert v-else-if="targetsError">
          <AlertDescription>{{ targetsError }}</AlertDescription>
        </Alert>
        <Empty v-else-if="!targets.length" class="border py-8">
          <EmptyHeader>
            <EmptyTitle>{{ $t('暂无可攻击的目标') }}</EmptyTitle>
            <EmptyDescription>{{ $t('可能仍处于加固期,或尚无其他队伍的环境就绪') }}</EmptyDescription>
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
      </CardContent>
    </Card>

    <FlagSubmit
      :competition-id="competition.id!"
      :competition-challenge-id="challenge.id!"
      multiple
      :title="$t('批量提交 Flag')"
      :description="$t('从其他队伍靶机拿到的 flag,每行一个,一次提交多个')"
    />
  </div>
</template>
