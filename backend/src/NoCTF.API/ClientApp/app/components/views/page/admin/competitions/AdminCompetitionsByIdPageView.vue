<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdPageViewState } from '~/features/routes/admin/competitions/useAdminCompetitionsByIdPage'

const viewProps = defineProps<{ state: AdminCompetitionsByIdPageViewState }>()
const { competition, role, loading, error, RoleLabel, navGroups, activePath, isProgressionPage, isWriteUpReview, usesPageScroll, CompetitionStatusBadge, GameModeBadge, AppWorkspaceNav } = toRefs(viewProps.state)
</script>

<template>
  <component :is="AppWorkspaceNav" v-if="competition" :groups="navGroups" :title="competition.title">
    <div :data-workspace-scroll-content="usesPageScroll ? undefined : ''" data-competition-management-workspace
      class="mx-auto flex w-full flex-col gap-6 px-4 pt-8 md:px-6"
      :class="[isProgressionPage || isWriteUpReview ? 'max-w-none' : 'max-w-6xl', usesPageScroll ? 'pb-8' : 'h-full min-h-0']">
      <div class="flex shrink-0 flex-wrap items-center justify-between gap-3">
        <div class="flex flex-wrap items-center gap-3">
          <h1 class="text-display text-2xl">{{ competition.title }}</h1>
          <component :is="GameModeBadge" :mode="competition.mode" />
          <component :is="CompetitionStatusBadge" :status="competition.status" />
          <Badge variant="outline">{{ $t('administration.label.myRole', { role: translate(RoleLabel[role]) }) }}</Badge>
        </div>
      </div>
      <ScrollSurface axis="y" :enabled="!usesPageScroll" :reset-key="activePath" class="w-full"
        :class="usesPageScroll ? 'overflow-visible' : 'min-h-0 flex-1 overscroll-contain pr-3'"
        :aria-label="$t('navigation.competitionAdmin')">
        <div :data-admin-writeup-review-workspace="isWriteUpReview ? '' : undefined"
          :class="isWriteUpReview ? 'h-full min-h-0 px-1 pb-1' : usesPageScroll ? 'px-1 pb-8' : 'p-4 pb-8'">
          <MotionSwap :identity="activePath" preset="film-up">
            <NuxtPage />
          </MotionSwap>
        </div>
      </ScrollSurface>
    </div>

  </component>

  <div v-else class="mx-auto flex max-w-7xl flex-col gap-6 px-4 py-8">
    <div v-if="loading" class="flex flex-col gap-4">
      <Skeleton class="h-10 w-64" />
      <Skeleton class="h-8 w-full max-w-xl" />
      <Skeleton class="h-64 w-full" />
    </div>
    <Alert v-else variant="destructive">
      <AlertDescription>{{ error ?? $t('common.error.loadingCompetitionFailed') }}</AlertDescription>
    </Alert>
  </div>
</template>
