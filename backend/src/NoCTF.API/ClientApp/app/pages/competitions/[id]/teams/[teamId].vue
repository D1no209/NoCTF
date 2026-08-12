<script setup lang="ts">
import { getTeamEndpoint } from '~/api'
import type { NoCtfapiEndpointsTeamsTeamResponse } from '~/api'

const route = useRoute()
const competitionId = route.params.id as string
const teamId = route.params.teamId as string

const team = ref<NoCtfapiEndpointsTeamsTeamResponse | null>(null)
const loading = ref(true)
const error = ref<string | null>(null)

onMounted(async () => {
  const { data, error: err } = await getTeamEndpoint({ path: { competitionId, teamId } })
  loading.value = false
  if (err || !data) {
    error.value = parseApiError(err, translate("加载队伍信息失败")).message
    return
  }
  team.value = data
})
</script>

<template>
  <div class="flex flex-col gap-6">
    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>

    <div v-else-if="loading" class="flex flex-col gap-4">
      <Skeleton class="h-10 w-1/3" />
      <Skeleton class="h-40 w-full" />
    </div>

    <template v-else-if="team">
      <div class="flex flex-wrap items-center gap-3">
        <Avatar class="size-12">
          <AvatarImage v-if="team.avatarUrl" :src="team.avatarUrl" :alt="team.name ?? ''" />
          <AvatarFallback>{{ team.name?.slice(0, 2) ?? '?' }}</AvatarFallback>
        </Avatar>
        <h2 class="text-xl font-semibold">{{ team.name }}</h2>
        <Badge
          :variant="team.registrationStatus === 'Approved' ? 'default' : team.registrationStatus === 'Rejected' ? 'destructive' : 'secondary'"
        >
          {{ teamRegistrationStatusLabel(team.registrationStatus) }}
        </Badge>
        <Badge variant="outline">{{ team.trackName ?? team.trackKey }}</Badge>
        <Badge v-if="team.isBanned" variant="destructive">{{ $t('已封禁') }}</Badge>
      </div>
      <p class="text-sm text-muted-foreground">{{ $t('报名时间：{time}', { time: formatDateTime(team.registeredAt) }) }}</p>

      <Card>
        <CardHeader>
          <CardTitle class="text-base">{{ $t('成员（{count}）', { count: team.memberIds?.length ?? 0 }) }}</CardTitle>
        </CardHeader>
        <CardContent>
          <TeamMembers :competition-id="competitionId" :team="team" />
        </CardContent>
      </Card>
    </template>
  </div>
</template>
