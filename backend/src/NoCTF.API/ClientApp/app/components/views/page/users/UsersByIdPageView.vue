<script setup lang="ts">
import { toRefs } from 'vue'
import type { UsersByIdPageViewState } from '~/features/routes/users/useUsersByIdPage'

const viewProps = defineProps<{ state: UsersByIdPageViewState }>()
const { ImagePlus, UserRound, profile, loading, error, isOwnProfile, coverUrl, modes, directionRows, recentCompetitions, modeChartOption, directionChartOption, coverInput, coverPending, coverEditorOpen, coverSourceFile, setCoverInputRef, selectCover, setCoverEditorOpen, uploadCover, reportCoverError, ProfileCoverCropDialog } = toRefs(viewProps.state)
</script>

<template>
  <div
    data-public-profile-page
    class="mx-auto grid h-full min-h-0 w-full max-w-[96rem] grid-rows-[minmax(0,0.618fr)_minmax(0,1fr)] gap-4 overflow-hidden px-4 py-4 md:px-6"
  >
    <Alert v-if="error" variant="destructive" class="self-start">
      <AlertDescription>{{ $message(error) }}</AlertDescription>
    </Alert>

    <template v-else-if="loading">
      <Skeleton class="h-full w-full rounded-[20px]" />
      <Skeleton class="h-full w-full rounded-[20px]" />
    </template>

    <template v-else-if="profile">
      <Card data-public-profile-card data-profile-identity-layer class="relative h-full min-h-0 w-full overflow-hidden p-0">
        <img
          v-if="coverUrl"
          :src="coverUrl"
          :alt="$t('profile.coverAlt', { user: profile.userName ?? '' })"
          class="absolute inset-0 size-full object-cover"
          decoding="async"
        >
        <div v-if="coverUrl" class="absolute inset-0 bg-card/70" aria-hidden="true" />

        <CardContent class="relative flex h-full min-h-0 flex-col justify-between gap-3 p-4 sm:p-5 lg:p-6">
          <div class="flex items-start justify-between gap-4">
            <Avatar data-public-profile-avatar class="size-16 shrink-0 sm:size-20">
              <AvatarImage v-if="profile.avatarUrl" :src="profile.avatarUrl" :alt="profile.userName ?? ''" />
              <AvatarFallback>{{ profile.userName?.slice(0, 2) ?? '?' }}</AvatarFallback>
            </Avatar>

            <div v-if="isOwnProfile" class="flex flex-col items-end gap-2">
              <FileInput :ref="setCoverInputRef" accept="image/jpeg,image/png,image/webp" class="sr-only" @change="selectCover" />
              <Button type="button" variant="outline" size="sm" :disabled="coverPending" @click="coverInput?.click()">
                <Spinner v-if="coverPending" data-icon="inline-start" />
                <component :is="ImagePlus" v-else data-icon="inline-start" />
                {{ coverUrl ? $t('profile.replaceCover') : $t('profile.uploadCover') }}
              </Button>
            </div>
          </div>

          <div class="max-w-3xl">
            <Badge variant="secondary" class="mb-2">
              <component :is="UserRound" class="size-3.5" aria-hidden="true" />
              {{ $t('profile.playerProfile') }}
            </Badge>
            <h1 class="text-display text-2xl sm:text-3xl">{{ profile.userName }}</h1>
            <p v-if="profile.description" class="mt-2 line-clamp-3 max-w-[70ch] whitespace-pre-line text-sm leading-6">
              {{ profile.description }}
            </p>
            <p v-else class="mt-2 text-sm text-muted-foreground">{{ $t('ui.thisUserHasNotFilledOutAProfileYet') }}</p>
          </div>

          <ScrollSurface v-if="profile.badges?.length" axis="x" class="max-w-3xl" :aria-label="$t('progression.publicBadges')">
            <div class="flex items-center gap-2 py-1">
            <div v-for="badge in profile.badges" :key="`${badge.competitionId}:${badge.id}`"
              class="flex shrink-0 items-center gap-2 rounded-lg border bg-background/75 px-2 py-1.5">
              <img :src="badge.imageUrl" :alt="badge.name" class="size-8 rounded object-cover" />
              <span class="max-w-36 truncate text-xs font-medium">{{ badge.name }}</span>
            </div>
            </div>
          </ScrollSurface>

          <div class="grid max-w-3xl grid-cols-3 gap-2 rounded-2xl bg-background/65 p-1.5 sm:gap-3 sm:p-2">
            <div class="px-2 py-1.5 sm:px-3">
              <p class="text-xs text-muted-foreground">{{ $t('profile.competitionsJoined') }}</p>
              <p class="font-mono text-lg font-semibold tabular-nums sm:text-xl">{{ profile.competitionCount ?? 0 }}</p>
            </div>
            <div class="px-2 py-1.5 sm:px-3">
              <p class="text-xs text-muted-foreground">{{ $t('profile.competitionsFinished') }}</p>
              <p class="font-mono text-lg font-semibold tabular-nums sm:text-xl">{{ profile.finishedCompetitionCount ?? 0 }}</p>
            </div>
            <div class="px-2 py-1.5 sm:px-3">
              <p class="text-xs text-muted-foreground">{{ $t('profile.successfulChallenges') }}</p>
              <p class="font-mono text-lg font-semibold tabular-nums sm:text-xl">{{ profile.successfulChallengeCount ?? 0 }}</p>
            </div>
          </div>
        </CardContent>
      </Card>

      <Card data-profile-technical-layer class="h-full min-h-0 w-full overflow-hidden py-4">
        <CardHeader class="shrink-0 px-5 pb-1">
          <CardTitle class="text-display text-xl">{{ $t('profile.competitionProfile') }}</CardTitle>
        </CardHeader>
        <CardContent class="grid min-h-0 flex-1 gap-6 overflow-hidden px-5 md:grid-cols-[minmax(18rem,0.72fr)_minmax(0,1.28fr)]">
          <section class="contents md:grid md:h-full md:min-h-0 md:min-w-0 md:grid-rows-2 md:gap-3" aria-labelledby="profile-participation-title">
            <div class="order-1 min-h-0 min-w-0 md:order-none">
              <h2 id="profile-participation-title" class="font-semibold">{{ $t('profile.competitionModes') }}</h2>
              <MiniChart v-if="modes.length" :option="modeChartOption" height="clamp(9rem, 20dvh, 11.5rem)" />
              <Empty v-else class="mt-3 min-h-36">
                <EmptyDescription>{{ $t('profile.noPublicCompetitionData') }}</EmptyDescription>
              </Empty>
              <ul class="sr-only">
                <li v-for="mode in modes" :key="mode.mode">{{ mode.mode }}: {{ mode.competitionCount ?? 0 }}</li>
              </ul>
            </div>

            <div class="order-3 min-h-0 min-w-0 md:order-none">
              <h2 id="profile-directions-title" class="font-semibold">{{ $t('profile.strongDirections') }}</h2>
              <MiniChart :option="directionChartOption" height="clamp(11rem, 23dvh, 14rem)" />
              <ul class="sr-only">
                <li v-for="direction in directionRows" :key="direction.direction">
                  {{ direction.label }}: {{ direction.successfulChallengeCount ?? 0 }}
                </li>
              </ul>
            </div>
          </section>

          <section class="order-2 flex h-full min-h-0 min-w-0 flex-col md:order-none" aria-labelledby="profile-recent-competitions-title">
            <h2 id="profile-recent-competitions-title" class="font-semibold">{{ $t('profile.recentCompetitions') }}</h2>
            <ScrollSurface v-if="recentCompetitions.length" axis="y" class="mt-3 min-h-0 flex-1 pr-2" :aria-label="$t('profile.recentCompetitions')">
              <div class="flex flex-col">
                <template v-for="(competition, index) in recentCompetitions" :key="competition.competitionId">
                  <Separator v-if="index" />
                  <NuxtLink
                    :to="competitionPath(competition.competitionId!)"
                    class="group flex min-w-0 items-center justify-between gap-4 rounded-xl px-3 py-4 transition-colors hover:bg-accent/40 focus-visible:bg-accent/40"
                  >
                    <span class="min-w-0">
                      <span class="block truncate font-medium group-hover:text-primary">{{ competition.title }}</span>
                      <span class="mt-1 block truncate text-xs text-muted-foreground">{{ competition.teamName }} · {{ competition.dateRange }}</span>
                    </span>
                    <span class="flex shrink-0 flex-col items-end gap-1">
                      <Badge variant="secondary">{{ competition.modeLabel }}</Badge>
                      <span class="text-xs text-muted-foreground">{{ competition.statusLabel }}</span>
                    </span>
                  </NuxtLink>
                </template>
              </div>
            </ScrollSurface>
            <Empty v-else class="mt-4 min-h-44">
              <EmptyDescription>{{ $t('profile.noRecentCompetitions') }}</EmptyDescription>
            </Empty>
          </section>
        </CardContent>
      </Card>
    </template>

    <component
      :is="ProfileCoverCropDialog"
      variant="profile-cover"
      :open="coverEditorOpen"
      :file="coverSourceFile"
      :saving="coverPending"
      @update:open="setCoverEditorOpen"
      @save="uploadCover"
      @error="reportCoverError"
    />
  </div>
</template>
