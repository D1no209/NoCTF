<script setup lang="ts">
import { listCompetitionTeamsEndpoint } from '~/api'
import type { NoCtfapiEndpointsTeamsTeamResponse } from '~/api'

const route = useRoute()
const competitionId = route.params.id as string

const teams = ref<NoCtfapiEndpointsTeamsTeamResponse[]>([])
const loading = ref(true)
const error = ref<string | null>(null)

onMounted(async () => {
  const { data, error: err } = await listCompetitionTeamsEndpoint({ path: { competitionId } })
  loading.value = false
  if (err || !data) {
    error.value = parseApiError(err, '加载队伍列表失败').message
    return
  }
  teams.value = data.items ?? []
})
</script>

<template>
  <div class="flex flex-col gap-6">
    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>

    <div v-if="loading" class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
      <Skeleton v-for="i in 6" :key="i" class="h-24 w-full" />
    </div>

    <Empty v-else-if="!teams.length" class="border py-12">
      <EmptyHeader>
        <EmptyTitle>暂无报名队伍</EmptyTitle>
        <EmptyDescription>成为第一支报名参赛的队伍吧</EmptyDescription>
      </EmptyHeader>
    </Empty>

    <div v-else class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
      <NuxtLink
        v-for="team in teams"
        :key="team.id"
        :to="`/competitions/${competitionId}/teams/${team.id}`"
      >
        <Card class="h-full transition-colors hover:border-primary/50">
          <CardHeader>
            <div class="flex items-center gap-3">
              <Avatar class="size-10">
                <AvatarImage v-if="team.avatarUrl" :src="team.avatarUrl" :alt="team.name ?? ''" />
                <AvatarFallback>{{ team.name?.slice(0, 2) ?? '?' }}</AvatarFallback>
              </Avatar>
              <div class="flex flex-col gap-1">
                <CardTitle class="text-base">{{ team.name }}</CardTitle>
                <div class="flex items-center gap-2">
                  <Badge
                    :variant="team.registrationStatus === TeamRegistrationStatus.Approved ? 'default' : team.registrationStatus === TeamRegistrationStatus.Rejected ? 'destructive' : 'secondary'"
                  >
                    {{ teamRegistrationStatusLabel(team.registrationStatus) }}
                  </Badge>
                  <span class="text-xs text-muted-foreground">{{ team.memberIds?.length ?? 0 }} 名成员</span>
                </div>
              </div>
            </div>
          </CardHeader>
        </Card>
      </NuxtLink>
    </div>
  </div>
</template>
