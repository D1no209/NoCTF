<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionsByIdProgressionPageViewState } from '~/features/routes/competitions/[id]/useCompetitionsByIdProgressionPage'

const props = defineProps<{ state: CompetitionsByIdProgressionPageViewState }>()
const {
  data, loading, error, nodes, edges, direction, initialFocusNodeId,
  currentProgressNodeId, highlightedNodeIds, highlightedEdgeIds,
  blockedMessage, selectedNode, selectedRequirements, challengeNodes,
  completedChallenges, badgeSheetOpen, selectedBadgeId, isExpanded,
  load, onNodeClick, closeSelectedNode, enterSelectedChallenge,
  toggleExpanded, openBadges, toggleBadgeDetails, ProgressionCanvas,
} = toRefs(props.state)
</script>

<template>
  <div data-progression-page class="flex min-h-0 w-full flex-1 flex-col">
    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ error }} <Button size="sm" variant="outline" @click="load">{{ $t('common.label.retry') }}</Button></AlertDescription>
    </Alert>
    <Skeleton v-if="loading" class="min-h-64 flex-1" />
    <template v-else-if="data">
      <Teleport to="body" :disabled="!isExpanded">
        <section data-progression-map-workspace :data-expanded="isExpanded"
          class="flex min-h-0 flex-1 flex-col overflow-hidden rounded-2xl bg-card/45 shadow-lg backdrop-blur-md">
          <header data-progression-toolbar class="flex shrink-0 flex-wrap items-center justify-between gap-2 px-4 py-3">
            <div class="flex min-w-0 flex-wrap items-center gap-3">
              <h2 class="text-display text-lg">{{ $t('progression.map') }}</h2>
              <Badge v-if="data.showPlayerMap" variant="secondary" class="font-mono tabular-nums">
                {{ $t('progression.progressCount', { completed: completedChallenges, total: challengeNodes.length }) }}
              </Badge>
            </div>
            <div class="flex flex-wrap items-center gap-2">
              <Button type="button" variant="secondary" size="sm" @click="openBadges">
                {{ $t('progression.badgeCount', { count: data.badges?.length ?? 0 }) }}
              </Button>
              <Button v-if="data.showPlayerMap" type="button" variant="secondary" size="sm"
                :aria-pressed="isExpanded" @click="toggleExpanded">
                {{ isExpanded ? $t('progression.exitExpandedMap') : $t('progression.expandMap') }}
              </Button>
            </div>
          </header>

          <div v-if="data.showPlayerMap" data-progression-map-surface class="relative min-h-0 flex-1">
            <component :is="ProgressionCanvas" v-model:nodes="nodes" v-model:edges="edges"
              read-only show-progress-controls :direction="direction"
              :focus-node-id="initialFocusNodeId" :current-progress-node-id="currentProgressNodeId"
              :highlighted-node-ids="highlightedNodeIds" :highlighted-edge-ids="highlightedEdgeIds"
              height="100%" @node-click="onNodeClick" />

            <aside v-if="selectedNode" data-progression-inspector data-scroll-surface data-scroll-axis="y"
              class="flex flex-col gap-4 rounded-xl bg-popover/95 p-4 shadow-lg backdrop-blur-md"
              :aria-label="$t('progression.nodeDetails')">
              <div class="flex items-start justify-between gap-2">
                <div class="min-w-0">
                  <h3 class="break-words text-base font-semibold">{{ selectedNode.data?.title }}</h3>
                  <p class="mt-1 text-sm text-muted-foreground">
                    {{ selectedNode.data?.kind === 1 ? $t('progression.badgeNode') : $t('progression.challengeNode') }}
                  </p>
                </div>
                <Button type="button" variant="ghost" size="sm" @click="closeSelectedNode">{{ $t('common.action.close') }}</Button>
              </div>

              <div v-if="selectedNode.data?.kind === 1" class="flex flex-col gap-3">
                <img v-if="selectedNode.data.imageUrl" :src="selectedNode.data.imageUrl" alt=""
                  class="size-16 rounded-lg object-cover" />
                <p v-if="selectedNode.data.description" class="break-words text-sm text-muted-foreground">
                  {{ selectedNode.data.description }}
                </p>
                <Badge :variant="selectedNode.data.complete ? 'default' : 'secondary'" class="w-fit">
                  {{ selectedNode.data.complete ? $t('progression.completed') : $t('progression.notEarned') }}
                </Badge>
                <p v-if="!selectedNode.data.requiresPrerequisites" class="text-sm text-muted-foreground">
                  {{ $t('progression.badgeAlwaysActive') }}
                </p>
              </div>

              <div v-else-if="selectedNode.data" class="flex flex-col gap-3">
                <Badge :variant="selectedNode.data.complete ? 'default' : 'secondary'" class="w-fit">
                  {{ selectedNode.data.complete ? $t('progression.completed') : selectedNode.data.active
                    ? selectedNode.data.visited ? $t('progression.inProgress') : $t('progression.available')
                    : $t('progression.locked') }}
                </Badge>
                <p v-if="selectedNode.data.complete && !selectedNode.data.active" class="text-sm text-warning">
                  {{ $t('progression.completedButLocked') }}
                </p>
                <p v-if="!selectedNode.data.requiresPrerequisites" class="text-sm text-muted-foreground">
                  {{ $t('progression.challengeAlwaysOpen') }}
                </p>
                <div class="flex flex-col gap-2">
                  <h4 class="text-sm font-medium">{{ $t('progression.predecessors') }}</h4>
                  <p v-if="!selectedRequirements.length" class="text-sm text-muted-foreground">
                    {{ $t('progression.noPrerequisites') }}
                  </p>
                  <ul v-else class="flex flex-col gap-2">
                    <li v-for="requirement in selectedRequirements" :key="requirement.id ?? undefined"
                      class="flex flex-wrap items-center justify-between gap-2 text-sm">
                      <span class="min-w-0 break-words">{{ requirement.title }}</span>
                      <Badge :variant="requirement.satisfied ? 'default' : 'secondary'">
                        {{ requirement.condition === 1 ? $t('progression.incomplete') : $t('progression.completed') }}
                        · {{ requirement.satisfied ? $t('progression.requirementMet') : $t('progression.requirementUnmet') }}
                      </Badge>
                    </li>
                  </ul>
                </div>
                <p v-if="blockedMessage" role="status" class="text-sm text-warning">{{ blockedMessage }}</p>
                <Button v-if="selectedNode.data.active" type="button" class="w-full" @click="enterSelectedChallenge">
                  {{ $t('progression.enterChallenge') }}
                </Button>
              </div>
            </aside>
          </div>
          <Empty v-else class="min-h-0 flex-1">
            <EmptyHeader>
              <EmptyTitle>{{ $t('progression.mapUnavailable') }}</EmptyTitle>
            </EmptyHeader>
          </Empty>
        </section>
      </Teleport>

      <Sheet v-model:open="badgeSheetOpen">
        <SheetContent :side="direction === 'DOWN' ? 'bottom' : 'right'"
          data-scroll-surface data-scroll-axis="y"
          class="max-h-[70dvh] overflow-y-auto data-[side=right]:h-full">
          <SheetHeader>
            <SheetTitle>{{ $t('progression.myTeamBadges') }}</SheetTitle>
            <SheetDescription>{{ $t('progression.badgeCount', { count: data.badges?.length ?? 0 }) }}</SheetDescription>
          </SheetHeader>
          <div v-if="data.badges?.length" class="flex flex-col gap-2 px-4 pb-6">
            <div v-for="badge in data.badges" :key="badge.id ?? undefined" class="flex flex-col">
              <Button type="button" variant="ghost" class="h-auto w-full justify-start gap-3 p-2 text-left whitespace-normal"
                :aria-expanded="selectedBadgeId === badge.id"
                :aria-controls="`progression-badge-details-${badge.id}`"
                @click="toggleBadgeDetails(badge.id!)">
                <img :src="badge.imageUrl ?? undefined" alt="" class="size-12 shrink-0 rounded-lg object-cover" />
                <span class="min-w-0 break-words font-medium">{{ badge.name }}</span>
              </Button>
              <div v-show="selectedBadgeId === badge.id" :id="`progression-badge-details-${badge.id}`"
                class="flex flex-col gap-2 px-2 pb-3 text-sm">
                <p class="font-medium text-primary">{{ $t('progression.earned') }}</p>
                <p v-if="badge.description" class="break-words text-muted-foreground">{{ badge.description }}</p>
              </div>
            </div>
          </div>
          <Empty v-else>
            <EmptyHeader><EmptyTitle>{{ $t('progression.noBadges') }}</EmptyTitle></EmptyHeader>
          </Empty>
        </SheetContent>
      </Sheet>
    </template>
  </div>
</template>
