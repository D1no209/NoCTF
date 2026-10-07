<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionsByIdTeamsIndexPageViewState } from '~/features/routes/competitions/[id]/teams/useCompetitionsByIdTeamsIndexPage'

const viewProps = defineProps<{ state: CompetitionsByIdTeamsIndexPageViewState }>()
const { competitionId, teams, loading, error, teamDisplayNames, initialized, page, pageLimit, pageCount, total, loadPage, setPageSize } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-6">
    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ $message(error) }}</AlertDescription>
    </Alert>

    <div v-if="loading" class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
      <Skeleton v-for="i in 6" :key="i" class="h-24 w-full" />
    </div>

    <Empty v-else-if="!error && !teams.length" class="border py-12">
      <EmptyHeader>
        <EmptyTitle>{{ $t('competitions.competitionsBy.description.thereRegistrationTeamYet') }}</EmptyTitle>
        <EmptyDescription>{{ $t('competitions.competitionsBy.description.firstTeamSignCompete') }}</EmptyDescription>
      </EmptyHeader>
    </Empty>

    <div v-else-if="teams.length" class="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
      <NuxtLink
        v-for="team in teams"
        :key="team.id"
        :to="`/competitions/${competitionId}/teams/${team.id}`"
        class="min-w-0"
        prefetch-on="interaction"
      >
        <Card class="h-full transition-colors hover:border-primary/50">
          <CardHeader>
            <div class="flex items-center gap-3">
              <Avatar class="size-10 shrink-0">
                <AvatarImage v-if="team.avatarUrl" :src="team.avatarUrl" :alt="team.name ?? ''" />
                <AvatarFallback>{{ team.name?.slice(0, 2) ?? '?' }}</AvatarFallback>
              </Avatar>
              <div class="flex min-w-0 flex-1 flex-col gap-1">
                <CardTitle class="text-base [overflow-wrap:anywhere]">{{ teamDisplayName(team, teamDisplayNames) }}</CardTitle>
                <div class="flex flex-wrap items-center gap-2">
                  <Badge
                    :variant="team.registrationStatus === 'Approved' ? 'default' : team.registrationStatus === 'Rejected' ? 'destructive' : 'secondary'"
                  >
                    {{ teamRegistrationStatusLabel(team.registrationStatus) }}
                  </Badge>
                  <Badge variant="outline" class="whitespace-normal text-left [overflow-wrap:anywhere]">{{ team.trackName ?? team.trackKey }}</Badge>
                  <span class="text-xs text-muted-foreground">{{ $t('competitions.label.members', { count: team.memberIds?.length ?? 0 }) }}</span>
                </div>
              </div>
            </div>
          </CardHeader>
        </Card>
      </NuxtLink>
    </div>
    <OffsetPagination
      v-if="initialized && total > 0"
      :page="page"
      :page-count="pageCount"
      :total="total"
      :limit="pageLimit"
      :loading="loading"
      @update:page="loadPage"
      @update:limit="setPageSize"
    />
  </div>
</template>
