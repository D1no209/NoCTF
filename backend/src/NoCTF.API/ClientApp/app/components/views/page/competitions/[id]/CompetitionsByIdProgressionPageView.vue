<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionsByIdProgressionPageViewState } from '~/features/routes/competitions/[id]/useCompetitionsByIdProgressionPage'

const props = defineProps<{ state: CompetitionsByIdProgressionPageViewState }>()
const { data, loading, error, nodes, edges, load, ProgressionCanvas } = toRefs(props.state)
</script>

<template>
  <div class="space-y-6 px-4 py-5 md:px-6">
    <header>
      <h2 class="text-display text-2xl">{{ $t('progression.title') }}</h2>
      <p class="text-sm text-muted-foreground">{{ $t('progression.playerDescription') }}</p>
    </header>
    <Alert v-if="error" variant="destructive"><AlertDescription>{{ error }} <Button size="sm" variant="outline" @click="load">{{ $t('ui.retry') }}</Button></AlertDescription></Alert>
    <div v-if="loading" class="h-64 animate-pulse rounded-lg bg-muted/30" />
    <template v-else-if="data">
      <section>
        <h3 class="mb-3 font-semibold">{{ $t('progression.myTeamBadges') }}</h3>
        <div v-if="data.badges?.length" class="flex flex-wrap gap-3">
          <div v-for="badge in data.badges" :key="badge.id" class="flex max-w-72 items-center gap-3 rounded-lg border bg-card p-3">
            <img :src="badge.imageUrl" :alt="badge.name" class="size-12 rounded object-cover" />
            <div><p class="font-medium">{{ badge.name }}</p><p class="text-xs text-muted-foreground">{{ badge.description }}</p></div>
          </div>
        </div>
        <p v-else class="text-sm text-muted-foreground">{{ $t('progression.noBadges') }}</p>
      </section>
      <section v-if="data.showPlayerMap" class="overflow-hidden rounded-lg border bg-card">
        <h3 class="border-b px-4 py-3 font-semibold">{{ $t('progression.map') }}</h3>
        <component :is="ProgressionCanvas" v-model:nodes="nodes" v-model:edges="edges" read-only height="35rem" />
      </section>
    </template>
  </div>
</template>
