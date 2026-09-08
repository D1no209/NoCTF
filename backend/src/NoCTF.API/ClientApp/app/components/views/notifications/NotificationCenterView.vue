<script setup lang="ts">
import { toRefs } from 'vue'
import type { NotificationCenterViewState } from '~/features/notifications/useNotificationCenter'

const viewProps = defineProps<{ state: NotificationCenterViewState }>()
const { ArrowRight, Bell, Mail, selected, thread, threadLoading, threadError, routeError, items, loading, error, hasMore, initialized, loadMore, sourceLabel, audienceLabel, categoryLabel, threadText, openNotification, closeDetail, actionPath, showAction } = toRefs(viewProps.state)
</script>

<template>
  <div class="flex flex-col gap-6">
    <header class="flex items-start gap-3">
      <div class="mt-0.5 grid size-9 shrink-0 place-items-center rounded-lg bg-primary/10 text-primary">
        <Bell class="size-4" aria-hidden="true" />
      </div>
      <div>
        <h1 class="text-display text-2xl">{{ $t('ui.messageCenter') }}</h1>
        <p class="text-sm text-muted-foreground">{{ $t('ui.officialAnnouncementsAndMessagesDirectlyRelatedToYourAccountTeam') }}</p>
      </div>
    </header>

    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ $message(error.message) }}</AlertDescription>
    </Alert>
    <Alert v-if="routeError" variant="destructive">
      <AlertDescription>{{ $message(routeError) }}</AlertDescription>
    </Alert>

    <div class="grid min-h-96 items-start gap-6 lg:grid-cols-[minmax(18rem,2fr)_minmax(24rem,3fr)]">
      <section :aria-label="$t('ui.notificationList')" class="min-w-0">
        <div v-if="loading && !initialized" class="flex flex-col gap-2">
          <Skeleton v-for="i in 5" :key="i" class="h-20 w-full" />
        </div>

        <Empty v-else-if="initialized && !items.length" class="border py-12">
          <EmptyHeader>
            <EmptyTitle>{{ $t('ui.noNotificationYet') }}</EmptyTitle>
            <EmptyDescription>{{ $t('ui.inquiryResponsesTeamStatusAndManagementRemindersWillAppearHere') }}</EmptyDescription>
          </EmptyHeader>
        </Empty>

        <ul v-else class="flex flex-col gap-2">
          <li v-for="notification in items" :key="notification.id">
            <ActionButton
              type="button"
              class="w-full rounded-lg border px-4 py-3 text-left transition-colors hover:border-primary/50 hover:bg-muted/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
              :class="selected?.id === notification.id ? 'border-primary bg-primary/5' : ''"
              @click="openNotification(notification)"
            >
              <div class="flex items-start justify-between gap-3">
                <div class="min-w-0">
                  <div class="mb-1.5 flex flex-wrap items-center gap-1.5">
                    <Badge variant="secondary" class="text-[0.6875rem]">{{ categoryLabel(notification) }}</Badge>
                    <span class="text-[0.6875rem] text-muted-foreground">{{ audienceLabel(notification) }}</span>
                  </div>
                  <p class="line-clamp-2 text-sm font-medium">{{ notificationTitle(notification) }}</p>
                  <p class="mt-1 font-mono text-xs tabular-nums text-muted-foreground">
                    {{ sourceLabel(notification) }} · {{ formatDateTime(notification.sentAt) }}
                  </p>
                </div>
              </div>
            </ActionButton>
          </li>
        </ul>

        <div v-if="hasMore" class="mt-4 flex justify-center">
          <Button variant="outline" :disabled="loading" @click="loadMore">
            <Spinner v-if="loading" data-icon="inline-start" /> {{ $t('ui.loadMore') }} </Button>
        </div>
      </section>

      <section :aria-label="$t('ui.notificationDetails')" class="min-w-0 rounded-xl border bg-card">
        <div v-if="threadLoading && !selected" class="flex flex-col gap-3 p-5" aria-live="polite">
          <Skeleton class="h-6 w-2/3" />
          <Skeleton class="h-4 w-1/3" />
          <Skeleton class="mt-3 h-28 w-full" />
        </div>
        <Empty v-else-if="!selected" class="py-16">
          <EmptyHeader>
            <EmptyMedia variant="icon"><Mail /></EmptyMedia>
            <EmptyTitle>{{ $t('ui.selectAMessageToViewDetails') }}</EmptyTitle>
            <EmptyDescription>{{ $t('ui.theMessageBodyProcessingEntryAndSubsequentStatusWillBe') }}</EmptyDescription>
          </EmptyHeader>
        </Empty>

        <template v-else>
          <div class="flex items-start justify-between gap-4 border-b px-5 py-4">
            <div class="min-w-0">
              <Badge v-if="selected.kind === 'CompetitionAnnouncement'" variant="secondary" class="mb-2">{{ $t('ui.officialAnnouncement') }}</Badge>
              <h3 class="text-lg font-semibold leading-snug">{{ notificationTitle(selected) }}</h3>
              <p class="mt-2 font-mono text-xs tabular-nums text-muted-foreground">
                {{ $t('ui.publishedBy', { source: sourceLabel(selected), time: formatDateTime(selected.sentAt) }) }}
              </p>
            </div>
            <Button type="button" variant="ghost" size="sm" class="shrink-0" @click="closeDetail"> {{ $t('ui.close') }} </Button>
          </div>

          <div class="flex flex-col gap-5 px-5 py-5">
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
            <div>
              <h4 class="mb-3 text-sm font-semibold">{{ $t('ui.followUpRecords') }}</h4>
              <Skeleton v-if="threadLoading" class="h-28 w-full" />
              <Alert v-else-if="threadError" variant="destructive">
                <AlertDescription>{{ $message(threadError) }}</AlertDescription>
              </Alert>
              <p v-else-if="thread.length <= 1" class="text-sm text-muted-foreground">{{ $t('ui.thereHasBeenNoFollowUpReplyOrStatusChange') }}</p>
              <ol v-else class="flex flex-col gap-4">
                <li v-for="entry in thread" :key="entry.id" class="grid grid-cols-[0.5rem_1fr] gap-3">
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
            </div>
          </div>
        </template>
      </section>
    </div>
  </div>
</template>
