<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionWorkspaceNavigationViewState } from '~/features/competition/useCompetitionWorkspaceNavigation'

const viewProps = defineProps<{ state: CompetitionWorkspaceNavigationViewState }>()
const { Compass, isActive, groups } = toRefs(viewProps.state)
</script>

<template>
  <Card as="nav" size="sm" class="gap-0 py-0" :aria-label="$t('ui.competitionNavigation')">
    <CardHeader class="flex flex-row items-center gap-2 px-4 py-3">
      <Compass class="size-4 text-primary" aria-hidden="true" />
      <CardTitle>{{ $t('ui.competitionNavigation') }}</CardTitle>
    </CardHeader>

    <ScrollSurface axis="y" class="min-h-0 flex-1" :aria-label="$t('ui.competitionNavigation')">
    <CardContent class="flex flex-col gap-4 px-3 pb-3">
      <section v-for="(group, groupIndex) in groups" :key="group.label ?? groupIndex" class="flex flex-col gap-2">
        <h3 v-if="group.label" class="px-1 text-[0.6875rem] font-medium uppercase tracking-[0.14em] text-muted-foreground">
          {{ group.label }}
        </h3>
        <div class="grid grid-cols-2 gap-2">
          <NuxtLink
            v-for="item in group.items"
            :key="item.to"
            :to="item.to"
            class="flex min-h-10 items-center gap-2 rounded-md px-3 py-2 text-xs font-medium transition-colors focus-visible:outline-none"
            :class="isActive(item)
              ? 'bg-primary text-primary-foreground shadow-sm'
              : 'bg-background/60 text-muted-foreground hover:bg-muted/70 hover:text-foreground focus-visible:shadow-[0_0_14px_color-mix(in_oklch,var(--ring)_22%,transparent)]'"
          >
            <component :is="item.icon" class="size-4 shrink-0" aria-hidden="true" />
            <span class="truncate">{{ item.label }}</span>
          </NuxtLink>
        </div>
      </section>
    </CardContent>
    </ScrollSurface>
  </Card>
</template>
