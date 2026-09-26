<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminCompetitionsByIdPageViewState } from '~/features/routes/admin/competitions/useAdminCompetitionsByIdPage'

const viewProps = defineProps<{ state: AdminCompetitionsByIdPageViewState }>()
const { Megaphone, competition, role, loading, error, canAnnounce, announcementOpen, announcementTitle, announcementBody, announcementAudience, announcementPending, announcementError, RoleLabel, publishAnnouncement, setAnnouncementOpen, navGroups, activePath, isProgressionPage, CompetitionStatusBadge, GameModeBadge, AppWorkspaceNav, onClickAnnouncementOpen, onInputAnnouncementError } = toRefs(viewProps.state)
</script>

<template>
  <component :is="AppWorkspaceNav" v-if="competition" :groups="navGroups" :title="competition.title">
    <div data-workspace-scroll-content data-competition-management-workspace class="mx-auto flex h-full min-h-0 w-full flex-col gap-6 px-4 pt-8 md:px-6" :class="isProgressionPage ? 'max-w-none' : 'max-w-6xl'">
      <div class="flex shrink-0 flex-wrap items-center justify-between gap-3">
        <div class="flex flex-wrap items-center gap-3">
          <h1 class="text-display text-2xl">{{ competition.title }}</h1>
          <component :is="GameModeBadge" :mode="competition.mode" />
          <component :is="CompetitionStatusBadge" :status="competition.status" />
          <Badge variant="outline">{{ $t('ui.myRole', { role: $t(RoleLabel[role]) }) }}</Badge>
        </div>
        <Button v-if="canAnnounce" variant="outline" @click="onClickAnnouncementOpen(true)">
          <Megaphone data-icon="inline-start" /> {{ $t('ui.postANotice') }} </Button>
      </div>
      <ScrollSurface axis="y" :reset-key="activePath" class="min-h-0 flex-1 overscroll-contain pr-3" :aria-label="$t('ui.competitionAdmin')">
        <div class="px-1 pb-8">
          <MotionSwap :identity="activePath" preset="film-up">
            <NuxtPage />
          </MotionSwap>
        </div>
      </ScrollSurface>
    </div>

    <Dialog :open="announcementOpen" @update:open="setAnnouncementOpen">
        <DialogContent data-scroll-surface class="max-h-[calc(100vh-2rem)] overflow-y-auto sm:max-w-2xl">
          <DialogHeader>
            <DialogTitle>{{ $t('ui.publishCompetitionNotice') }}</DialogTitle>
            <DialogDescription>{{ $t('ui.sendAPermanentNotificationToAllParticipantsOrEventStaff') }}</DialogDescription>
          </DialogHeader>
          <FieldGroup>
            <Field>
              <FieldLabel>{{ $t('ui.notificationObject') }}</FieldLabel>
              <Select v-model="announcementAudience">
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectGroup>
                    <SelectItem value="Participants">{{ $t('ui.allContestants') }}</SelectItem>
                    <SelectItem value="Collaborators">{{ $t('ui.eventStaff') }}</SelectItem>
                  </SelectGroup>
                </SelectContent>
              </Select>
            </Field>
            <Field>
              <FieldLabel for="announcement-title">{{ $t('ui.title') }}</FieldLabel>
              <Input
                id="announcement-title"
                v-model="announcementTitle"
                maxlength="160"
                @input="onInputAnnouncementError(null)"
              />
            </Field>
            <Field>
              <FieldLabel for="announcement-body">{{ $t('ui.content') }}</FieldLabel>
              <Textarea
                id="announcement-body"
                v-model="announcementBody"
                class="min-h-40"
                rows="8"
                maxlength="16000"
                @input="onInputAnnouncementError(null)"
              />
            </Field>
            <p v-if="announcementError" role="alert" class="text-sm text-destructive">
              {{ $message(announcementError) }}
            </p>
          </FieldGroup>
          <DialogFooter>
            <Button variant="outline" :disabled="announcementPending" @click="setAnnouncementOpen(false)"> {{ $t('ui.cancel') }} </Button>
            <Button :disabled="announcementPending" @click="publishAnnouncement">
              <Spinner v-if="announcementPending" data-icon="inline-start" /> {{ $t('ui.postANotice') }} </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
  </component>

  <div v-else class="mx-auto flex max-w-7xl flex-col gap-6 px-4 py-8">
    <div v-if="loading" class="flex flex-col gap-4">
      <Skeleton class="h-10 w-64" />
      <Skeleton class="h-8 w-full max-w-xl" />
      <Skeleton class="h-64 w-full" />
    </div>
    <Alert v-else variant="destructive">
      <AlertDescription>{{ error ?? $t('ui.loadingCompetitionFailed') }}</AlertDescription>
    </Alert>
  </div>
</template>
