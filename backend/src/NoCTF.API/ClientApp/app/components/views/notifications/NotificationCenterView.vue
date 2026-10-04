<script setup lang="ts">
import { toRefs } from 'vue'
import type { NotificationCenterViewState } from '~/features/notifications/useNotificationCenter'

const viewProps = defineProps<{ state: NotificationCenterViewState }>()
const { ArrowRight, Bell, Mail, selected, selectedId, thread, threadLoading, threadError, routeError, notificationOptions, loading, error, hasMore, initialized, loadMore, sourceLabel, audienceLabel, categoryLabel, threadText, selectNotification, closeDetail, actionPath, showAction } = toRefs(viewProps.state)
</script>

<template>
  <div class="notification-center-page mx-auto flex h-full w-full flex-col gap-6 py-8">
    <header class="flex items-center gap-3">
      <div class="grid size-9 shrink-0 place-items-center rounded-lg bg-primary/10 text-primary">
        <Bell class="size-4" aria-hidden="true" />
      </div>
      <h1 class="text-display text-2xl">{{ $t('notifications.label.messageCenter') }}</h1>
    </header>

    <div class="notification-center-layout">
      <div class="min-w-0">
        <ChoiceSidebar
          :items="notificationOptions"
          :model-value="selectedId"
          :loading="loading && !initialized"
          :label="$t('notifications.label.notificationList')"
          :loading-label="$t('notifications.label.notificationList')"
          :empty-label="$t('notifications.label.notificationYet')"
          controls="notification-center-detail"
          @update:model-value="selectNotification"
        >
          <template #header>
            <header class="pr-10">
              <h2 class="text-base font-semibold">{{ $t('notifications.label.notificationList') }}</h2>
            </header>
          </template>
          <template #feedback>
            <Alert v-if="error" variant="destructive">
              <AlertDescription>{{ $message(error.message) }}</AlertDescription>
            </Alert>
          </template>
          <template #item="{ item }">
            <span class="flex min-w-0 flex-1 flex-col gap-2">
              <span class="flex flex-wrap items-center gap-1.5">
                <Badge variant="secondary" class="text-[0.6875rem]">{{ categoryLabel(item.notification) }}</Badge>
                <span class="text-[0.6875rem] text-muted-foreground">{{ audienceLabel(item.notification) }}</span>
              </span>
              <span class="line-clamp-2 text-sm font-semibold">{{ item.label }}</span>
              <span class="font-mono text-xs font-normal tabular-nums text-muted-foreground">
                {{ sourceLabel(item.notification) }} · {{ formatDateTime(item.notification.sentAt) }}
              </span>
            </span>
          </template>
          <template #footer>
            <Button v-if="hasMore" variant="outline" class="mr-10 shrink-0" :disabled="loading" @click="loadMore">
              <Spinner v-if="loading" data-icon="inline-start" /> {{ $t('common.label.load') }}
            </Button>
          </template>
        </ChoiceSidebar>
      </div>

      <Card id="notification-center-detail" class="notification-detail-card h-full min-h-0 gap-0 py-0" :aria-label="$t('notifications.label.notificationDetails')">
        <MotionSwap :identity="selectedId || ''" preset="film-up">
          <div class="flex h-full min-h-0 flex-col">
            <Alert v-if="routeError" variant="destructive">
              <AlertDescription>{{ $message(routeError) }}</AlertDescription>
            </Alert>

            <div v-if="threadLoading && !selected" class="flex flex-col gap-3 p-6" aria-live="polite">
              <Skeleton class="h-6 w-2/3" />
              <Skeleton class="h-4 w-1/3" />
              <Skeleton class="mt-3 h-28 w-full" />
            </div>
            <Empty v-else-if="!selected" class="flex-1 py-16">
              <EmptyHeader>
                <EmptyMedia variant="icon"><Mail /></EmptyMedia>
                <EmptyTitle>{{ $t('notifications.notificationCenter.description.selectMessageViewDetails') }}</EmptyTitle>
                <EmptyDescription>{{ $t('notifications.notificationCenter.description.messageBodyProcessingEntry') }}</EmptyDescription>
              </EmptyHeader>
            </Empty>

            <template v-else>
              <header class="flex shrink-0 items-start justify-between gap-4 px-6 py-5">
                <div class="min-w-0">
                  <Badge v-if="selected.kind === 'CompetitionAnnouncement'" variant="secondary" class="mb-2">{{ $t('notifications.label.officialAnnouncement') }}</Badge>
                  <h2 class="text-lg font-semibold leading-snug">{{ notificationTitle(selected) }}</h2>
                  <p class="mt-2 font-mono text-xs tabular-nums text-muted-foreground">
                    {{ $t('notifications.label.published', { source: sourceLabel(selected), time: formatDateTime(selected.sentAt) }) }}
                  </p>
                </div>
                <Button type="button" variant="ghost" size="sm" class="shrink-0" @click="closeDetail">{{ $t('common.action.close') }}</Button>
              </header>

              <ScrollSurface axis="y" class="min-h-0 flex-1" :aria-label="$t('notifications.label.notificationDetails')">
                <div class="flex flex-col gap-5 px-6 py-5">
                  <p v-if="notificationBody(selected)" class="max-w-[72ch] text-sm leading-7 whitespace-pre-wrap">
                    {{ notificationBody(selected) }}
                  </p>

                  <Button v-if="showAction" as-child class="w-fit">
                    <NuxtLink :to="actionPath ?? '/notifications'">
                      {{ notificationActionLabel(selected) }}
                      <ArrowRight data-icon="inline-end" />
                    </NuxtLink>
                  </Button>

                  <Separator />
                  <section :aria-label="$t('notifications.label.followRecords')">
                    <h3 class="mb-3 text-sm font-semibold">{{ $t('notifications.label.followRecords') }}</h3>
                    <Skeleton v-if="threadLoading" class="h-28 w-full" />
                    <Alert v-else-if="threadError" variant="destructive">
                      <AlertDescription>{{ $message(threadError) }}</AlertDescription>
                    </Alert>
                    <p v-else-if="thread.length <= 1" class="text-sm text-muted-foreground">{{ $t('notifications.notificationCenter.description.thereFollowReplyStatus') }}</p>
                    <ol v-else class="flex flex-col gap-4">
                      <li v-for="entry in thread" :key="entry.id ?? undefined" class="grid grid-cols-[0.5rem_1fr] gap-3">
                        <span class="mt-1.5 size-2 rounded-full bg-primary" aria-hidden="true" />
                        <div class="min-w-0">
                          <div class="flex flex-wrap items-center gap-x-2 gap-y-1 font-mono text-xs tabular-nums text-muted-foreground">
                            <span class="font-medium text-foreground">{{ sourceLabel(entry) }}</span>
                            <span>{{ formatDateTime(entry.sentAt) }}</span>
                          </div>
                          <p class="mt-1 max-w-[72ch] text-sm leading-6 whitespace-pre-wrap">{{ threadText(entry) }}</p>
                        </div>
                      </li>
                    </ol>
                  </section>
                </div>
              </ScrollSurface>
            </template>
          </div>
        </MotionSwap>
      </Card>
    </div>
  </div>
</template>
