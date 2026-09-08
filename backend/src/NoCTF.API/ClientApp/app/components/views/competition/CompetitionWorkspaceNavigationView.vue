<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionWorkspaceNavigationViewState } from '~/features/competition/useCompetitionWorkspaceNavigation'

const viewProps = defineProps<{ state: CompetitionWorkspaceNavigationViewState }>()
const { Compass, isActive, groups } = toRefs(viewProps.state)
</script>

<template>
  <nav class="rounded-xl border bg-card" :aria-label="$t('ui.competitionNavigation')">
    <header class="flex items-center gap-2 border-b px-4 py-3">
      <Compass class="size-4 text-primary" aria-hidden="true" />
      <h2 class="text-sm font-semibold">{{ $t('ui.competitionNavigation') }}</h2>
    </header>

    <div class="flex flex-col gap-4 p-3">
      <section v-for="(group, groupIndex) in groups" :key="group.label ?? groupIndex" class="flex flex-col gap-2">
        <h3 v-if="group.label" class="px-1 text-[0.6875rem] font-medium uppercase tracking-[0.14em] text-muted-foreground">
          {{ group.label }}
        </h3>
        <div class="grid grid-cols-2 gap-2">
          <NuxtLink
            v-for="item in group.items"
            :key="item.to"
            :to="item.to"
            class="flex min-h-10 items-center gap-2 rounded-md border px-3 py-2 text-xs font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
            :class="isActive(item)
              ? 'border-primary bg-primary text-primary-foreground'
              : 'border-border bg-background text-muted-foreground hover:border-primary/50 hover:text-foreground'"
          >
            <component :is="item.icon" class="size-4 shrink-0" aria-hidden="true" />
            <span class="truncate">{{ item.label }}</span>
          </NuxtLink>
        </div>
      </section>
    </div>
  </nav>
</template>
