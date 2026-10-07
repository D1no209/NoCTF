<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionsByIdTeamsIndexPageViewState } from '~/features/routes/competitions/[id]/teams/useCompetitionsByIdTeamsIndexPage'

const viewProps = defineProps<{ state: CompetitionsByIdTeamsIndexPageViewState }>()
const { competitionId, TeamMembers, teams, loading, error, teamDisplayNames, initialized, page, pageLimit, pageCount, total, loadPage, setPageSize } = toRefs(viewProps.state)
</script>

<template>
  <div class="mx-auto flex min-h-0 w-full max-w-6xl flex-1 flex-col gap-4">
    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ $message(error) }}</AlertDescription>
    </Alert>

    <Card class="min-h-0 flex-1 gap-0">
      <CardHeader class="flex flex-row items-center gap-3 pb-4">
        <CardTitle class="text-lg">{{ $t('common.label.teams') }}</CardTitle>
        <Badge v-if="initialized" variant="secondary" class="font-mono tabular-nums">{{ total }}</Badge>
      </CardHeader>
      <CardContent class="flex min-h-0 flex-1 flex-col px-0">
        <div class="hidden shrink-0 grid-cols-[minmax(0,1fr)_10rem_5rem_7rem_1rem] items-center gap-4 px-4 pb-3 text-xs text-muted-foreground md:grid" aria-hidden="true">
          <span>{{ $t('competitions.label.teamName') }}</span>
          <span>{{ $t('common.label.tracks') }}</span>
          <span>{{ $t('common.label.members') }}</span>
          <span>{{ $t('common.label.status') }}</span>
        </div>
        <Separator />
        <ScrollSurface axis="y" :reset-key="`${page}:${pageLimit}`" class="min-h-0 flex-1" :aria-label="$t('common.label.teams')">
          <div v-if="loading" class="flex flex-col gap-3 p-4">
            <Skeleton v-for="i in 6" :key="i" class="h-16 w-full" />
          </div>
          <Empty v-else-if="!error && !teams.length" class="py-12">
            <EmptyHeader>
              <EmptyTitle>{{ $t('competitions.competitionsBy.description.thereRegistrationTeamYet') }}</EmptyTitle>
              <EmptyDescription>{{ $t('competitions.competitionsBy.description.firstTeamSignCompete') }}</EmptyDescription>
            </EmptyHeader>
          </Empty>
          <Accordion v-else-if="teams.length" :key="`${page}:${pageLimit}`" type="single" collapsible>
            <AccordionItem v-for="team in teams" :key="team.id" v-slot="{ open }" :value="team.id ?? ''">
              <AccordionTrigger class="items-center gap-4 rounded-none px-4 py-4 hover:no-underline">
                <span class="grid min-w-0 flex-1 items-center gap-4 md:grid-cols-[minmax(0,1fr)_10rem_5rem_7rem]">
                  <span class="flex min-w-0 items-center gap-3">
                    <Avatar class="size-10 shrink-0">
                      <AvatarImage v-if="team.avatarUrl" :src="team.avatarUrl" :alt="team.name ?? ''" />
                      <AvatarFallback>{{ team.name?.slice(0, 2) ?? '?' }}</AvatarFallback>
                    </Avatar>
                    <span class="flex min-w-0 flex-col gap-1">
                      <span class="text-base font-semibold [overflow-wrap:anywhere]">{{ teamDisplayName(team, teamDisplayNames) }}</span>
                      <span class="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs font-normal text-muted-foreground md:hidden">
                        <span class="[overflow-wrap:anywhere]">{{ team.trackName ?? team.trackKey }}</span>
                        <span>{{ $t('competitions.label.members', { count: team.memberIds?.length ?? 0 }) }}</span>
                        <Badge :variant="team.isBanned || team.registrationStatus === 'Rejected' ? 'destructive' : 'secondary'">
                          {{ team.isBanned ? $t('common.label.banned') : teamRegistrationStatusLabel(team.registrationStatus) }}
                        </Badge>
                      </span>
                    </span>
                  </span>
                  <span class="hidden text-sm font-normal text-muted-foreground [overflow-wrap:anywhere] md:block">{{ team.trackName ?? team.trackKey }}</span>
                  <span class="hidden font-mono text-sm font-normal tabular-nums text-muted-foreground md:block">{{ team.memberIds?.length ?? 0 }}</span>
                  <span class="hidden md:block">
                    <Badge :variant="team.isBanned || team.registrationStatus === 'Rejected' ? 'destructive' : 'secondary'">
                      {{ team.isBanned ? $t('common.label.banned') : teamRegistrationStatusLabel(team.registrationStatus) }}
                    </Badge>
                  </span>
                </span>
              </AccordionTrigger>
              <AccordionContent class="px-4 pb-5 pt-1 sm:pl-[4.75rem]">
                <div class="flex flex-col gap-3">
                  <div class="flex flex-wrap items-center justify-between gap-2">
                    <h3 class="text-sm font-medium">{{ $t('common.label.members.teamPageView', { count: team.memberIds?.length ?? 0 }) }}</h3>
                    <Button variant="ghost" size="sm" as-child>
                      <NuxtLink :to="`/competitions/${competitionId}/teams/${team.id}`" prefetch-on="interaction">
                        {{ $t('common.label.teamDetails') }}
                      </NuxtLink>
                    </Button>
                  </div>
                  <div class="max-w-2xl">
                    <component v-if="open" :is="TeamMembers" :competition-id="competitionId" :team="team" :can-manage="false" />
                  </div>
                </div>
              </AccordionContent>
              <Separator />
            </AccordionItem>
          </Accordion>
        </ScrollSurface>
      </CardContent>
      <CardFooter v-if="initialized && total > 0" class="shrink-0 pt-4">
        <OffsetPagination
          class="w-full"
          :page="page"
          :page-count="pageCount"
          :total="total"
          :limit="pageLimit"
          :loading="loading"
          @update:page="loadPage"
          @update:limit="setPageSize"
        />
      </CardFooter>
    </Card>
  </div>
</template>
