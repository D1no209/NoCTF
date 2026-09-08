<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionsIndexPageViewState } from '~/features/routes/competitions/useCompetitionsIndexPage'

const viewProps = defineProps<{ state: CompetitionsIndexPageViewState }>()
const { loading, error, running, upcoming, finished, tab, CompetitionCard } = toRefs(viewProps.state)
</script>

<template>
  <div class="mx-auto flex max-w-7xl flex-col gap-6 px-4 py-10 md:px-6">
    <div>
      <h1 class="text-display text-2xl">{{ $t('ui.competitions') }}</h1>
      <p class="text-sm text-muted-foreground">{{ $t('ui.browseOpenCompetitionsOnThePlatformSignUpAndEnter') }}</p>
    </div>

    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ $message(error) }}</AlertDescription>
    </Alert>

    <div v-if="loading" class="grid gap-5 md:grid-cols-2 xl:grid-cols-3">
      <Skeleton v-for="i in 4" :key="i" class="h-44 w-full" />
    </div>

    <Tabs v-else v-model="tab">
      <TabsList>
        <TabsTrigger value="running">{{ $t('ui.running3', { count: running.length }) }}</TabsTrigger>
        <TabsTrigger value="upcoming">{{ $t('ui.upcoming', { count: upcoming.length }) }}</TabsTrigger>
        <TabsTrigger value="finished">{{ $t('ui.finished2', { count: finished.length }) }}</TabsTrigger>
      </TabsList>
      <TabsContent value="running">
        <div v-if="running.length" class="grid gap-5 md:grid-cols-2 xl:grid-cols-3">
          <component :is="CompetitionCard" v-for="c in running" :key="c.id" :competition="c" />
        </div>
        <Empty v-else class="border border-dashed py-12">
          <EmptyHeader>
            <EmptyTitle>{{ $t('ui.thereAreNoOngoingContests') }}</EmptyTitle>
            <EmptyDescription>{{ $t('ui.checkOutTheUpcomingCompetitionsAndSignUpInAdvance') }}</EmptyDescription>
          </EmptyHeader>
        </Empty>
      </TabsContent>
      <TabsContent value="upcoming">
        <div v-if="upcoming.length" class="grid gap-5 md:grid-cols-2 xl:grid-cols-3">
          <component :is="CompetitionCard" v-for="c in upcoming" :key="c.id" :competition="c" />
        </div>
        <Empty v-else class="border border-dashed py-12">
          <EmptyHeader>
            <EmptyTitle>{{ $t('ui.thereAreNoUpcomingCompetitionsYet') }}</EmptyTitle>
          </EmptyHeader>
        </Empty>
      </TabsContent>
      <TabsContent value="finished">
        <div v-if="finished.length" class="grid gap-5 md:grid-cols-2 xl:grid-cols-3">
          <component :is="CompetitionCard" v-for="c in finished" :key="c.id" :competition="c" />
        </div>
        <Empty v-else class="border border-dashed py-12">
          <EmptyHeader>
            <EmptyTitle>{{ $t('ui.thereAreNoCompletedContestsYet') }}</EmptyTitle>
          </EmptyHeader>
        </Empty>
      </TabsContent>
    </Tabs>
  </div>
</template>
