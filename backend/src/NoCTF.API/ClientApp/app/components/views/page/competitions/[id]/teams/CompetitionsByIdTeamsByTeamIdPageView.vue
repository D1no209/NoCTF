<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionsByIdTeamsByTeamIdPageViewState } from '~/features/routes/competitions/[id]/teams/useCompetitionsByIdTeamsByTeamIdPage'

const viewProps = defineProps<{ state: CompetitionsByIdTeamsByTeamIdPageViewState }>()
const { competitionId, team, loading, error, TeamMembers } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-6">
    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ $message(error) }}</AlertDescription>
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
        <Badge v-if="team.isBanned" variant="destructive">{{ $t('common.label.banned') }}</Badge>
      </div>
      <p class="text-sm text-muted-foreground">{{ $t('competitions.label.registered', { time: formatDateTime(team.registeredAt) }) }}</p>

      <Card>
        <CardHeader>
          <CardTitle class="text-base">{{ $t('common.label.members.teamPageView', { count: team.memberIds?.length ?? 0 }) }}</CardTitle>
        </CardHeader>
        <CardContent>
          <component :is="TeamMembers" :competition-id="competitionId" :team="team" />
        </CardContent>
      </Card>
    </template>
  </div>
</template>
