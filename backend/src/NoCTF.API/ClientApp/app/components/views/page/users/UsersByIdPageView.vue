<script setup lang="ts">
import { toRefs } from 'vue'
import type { UsersByIdPageViewState } from '~/features/routes/users/useUsersByIdPage'

const viewProps = defineProps<{ state: UsersByIdPageViewState }>()
const { ImagePlus, UserRound, profile, loading, error, isOwnProfile, coverUrl, modes, modeRows, directionRows, recentCompetitions, modeChartOption, directionChartOption, coverInput, coverPending, coverEditorOpen, coverSourceFile, setCoverInputRef, selectCover, setCoverEditorOpen, uploadCover, reportCoverError, ProfileCoverCropDialog } = toRefs(viewProps.state)
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
        <CardContent data-scroll-surface data-scroll-axis="y"
          class="grid min-h-0 flex-1 gap-5 overflow-y-auto px-5 lg:grid-cols-[minmax(10rem,0.55fr)_minmax(20rem,1fr)_minmax(18rem,1.12fr)] lg:overflow-hidden">
          <section class="contents" aria-labelledby="profile-participation-title">
            <div class="flex min-h-0 min-w-0 flex-col">
              <h2 id="profile-participation-title" class="font-semibold">{{ $t('profile.competitionModes') }}</h2>
              <div v-if="modes.length" class="h-36 min-h-0 lg:h-auto lg:flex-1">
                <MiniChart :option="modeChartOption" height="100%" />
              </div>
              <ul v-if="modeRows.length" class="grid shrink-0 grid-cols-2 gap-x-3 gap-y-1.5 pb-2 text-xs">
                <li v-for="mode in modeRows" :key="mode.mode" class="flex min-w-0 items-center gap-1.5">
                  <span :class="mode.colorClass" class="size-2 shrink-0 rounded-full" aria-hidden="true" />
                  <span class="min-w-0 truncate">{{ mode.label }}</span>
                  <span class="ml-auto font-mono tabular-nums">{{ mode.competitionCount }}</span>
                </li>
              </ul>
              <Empty v-else class="mt-3 min-h-36">
                <EmptyDescription>{{ $t('profile.noPublicCompetitionData') }}</EmptyDescription>
              </Empty>
              <ul class="sr-only">
                <li v-for="mode in modes" :key="mode.mode">{{ mode.mode }}: {{ mode.competitionCount ?? 0 }}</li>
              </ul>
            </div>

            <div class="flex min-h-0 min-w-0 flex-col">
              <h2 id="profile-directions-title" class="font-semibold">{{ $t('profile.strongDirections') }}</h2>
              <div v-if="directionRows.length >= 3" class="h-72 min-h-0 lg:h-auto lg:flex-1">
                <MiniChart :option="directionChartOption" color-token="--primary" height="100%" />
              </div>
              <ul v-else-if="directionRows.length" class="mt-4 flex flex-col gap-2">
                <li v-for="direction in directionRows" :key="direction.direction"
                  class="flex items-center justify-between gap-4 text-sm">
                  <span>{{ direction.label }}</span>
                  <Badge variant="secondary" class="font-mono tabular-nums">{{ direction.successfulChallengeCount }}</Badge>
                </li>
              </ul>
              <Empty v-else class="mt-3 min-h-36">
                <EmptyDescription>{{ $t('profile.noPublicCompetitionData') }}</EmptyDescription>
              </Empty>
              <ul class="sr-only">
                <li v-for="direction in directionRows" :key="direction.direction">
                  {{ direction.label }}: {{ direction.successfulChallengeCount ?? 0 }}
                </li>
              </ul>
            </div>
          </section>

          <section class="flex h-full min-h-0 min-w-0 flex-col" aria-labelledby="profile-recent-competitions-title">
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

            <section v-if="profile.badges?.length" data-profile-earned-badges class="mt-4 shrink-0" aria-labelledby="profile-badges-title">
              <Separator class="mb-3" />
              <div class="flex items-center justify-between gap-3">
                <h2 id="profile-badges-title" class="font-semibold">{{ $t('profile.earnedBadges') }}</h2>
                <Badge variant="secondary" class="font-mono tabular-nums">{{ profile.badges.length }}</Badge>
              </div>
              <ScrollSurface axis="x" class="mt-2 w-full" :aria-label="$t('profile.earnedBadges')">
                <div class="flex items-start gap-2 pb-2">
                  <Popover v-for="badge in profile.badges" :key="`${badge.competitionId}:${badge.id}`">
                    <PopoverTrigger as-child>
                      <Button type="button" variant="secondary" class="h-auto w-36 shrink-0 flex-col gap-2 p-3 whitespace-normal">
                        <img :src="badge.imageUrl" alt="" class="size-14 rounded-lg object-contain" />
                        <span class="line-clamp-2 w-full break-words text-center text-xs font-medium">{{ badge.name }}</span>
                      </Button>
                    </PopoverTrigger>
                    <PopoverContent class="w-72 max-w-[calc(100vw-2rem)]" align="start">
                      <img :src="badge.imageUrl" alt="" class="mx-auto mb-3 size-20 rounded-xl object-contain" />
                      <PopoverHeader>
                        <PopoverTitle>{{ badge.name }}</PopoverTitle>
                        <PopoverDescription>{{ badge.competitionTitle }}</PopoverDescription>
                      </PopoverHeader>
                      <p v-if="badge.description" class="mt-3 break-words text-sm">{{ badge.description }}</p>
                    </PopoverContent>
                  </Popover>
                </div>
              </ScrollSurface>
            </section>
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
