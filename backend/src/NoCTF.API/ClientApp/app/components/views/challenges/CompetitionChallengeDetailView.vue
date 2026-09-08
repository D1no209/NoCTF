<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionChallengeDetailViewState } from '~/features/challenges/useCompetitionChallengeDetail'

const viewProps = defineProps<{ state: CompetitionChallengeDetailViewState }>()
const { Dice5, Download, FileDown, ctx, isLoggedIn, user, challenge, loading, error, attachments, attachmentDeliveryPolicy, attachmentsLoading, attachmentError, downloading, historyRefreshKey, refreshSubmissionHistory, loadAttachments, downloadAttachment, downloadRandom, mode, ChallengeHints, ChallengeSubmissionHistory, AwdPanel, AwdpPanel, CtfPanel, KohPanel, competitionId, competitionChallengeId } = toRefs(viewProps.state)
</script>

<template>
  <section class="min-w-0" aria-live="polite">
    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ $message(error) }}</AlertDescription>
    </Alert>

    <div v-else-if="loading" class="flex flex-col gap-4">
      <Skeleton class="h-8 w-1/2" />
      <Skeleton class="h-40 w-full" />
      <Skeleton class="h-48 w-full" />
    </div>

    <div v-else-if="challenge" class="flex flex-col">
      <header class="flex flex-wrap items-center gap-3 border-b pb-5">
        <h2 class="text-display text-2xl">{{ challenge.title }}</h2>
        <Badge variant="outline" :class="directionBadgeClass(challenge.direction)">
          {{ directionLabel(challenge.direction) }}
        </Badge>
        <Badge v-if="mode === 'Awdp'" variant="secondary">{{ $t('ui.scoresSettleByRound') }}</Badge>
      </header>

      <section v-if="challenge.description" class="border-b py-5" :aria-label="challenge.title">
        <MarkdownContent :source="challenge.description" class="text-foreground/90" />
      </section>

      <section
        v-if="isLoggedIn && (attachmentsLoading || attachmentError || attachments.length || attachmentDeliveryPolicy === 'RandomOnePerTeam')"
        class="border-b py-5"
        aria-labelledby="challenge-attachments-title"
      >
        <div class="flex items-center justify-between gap-3">
            <h3 id="challenge-attachments-title" class="text-sm font-semibold">{{ $t('ui.accessories') }}</h3>
            <Button
              v-if="!attachmentsLoading && !attachmentError && attachmentDeliveryPolicy === 'RandomOnePerTeam'"
              variant="outline"
              size="sm"
              :disabled="downloading"
              @click="downloadRandom"
            >
              <Dice5 data-icon="inline-start" /> {{ $t('ui.downloadAttachment') }}
            </Button>
        </div>
          <Skeleton v-if="attachmentsLoading" class="mt-3 h-12 w-full" />
          <Alert v-else-if="attachmentError" variant="destructive" class="mt-3">
            <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
              <span>{{ $message(attachmentError) }}</span>
              <Button type="button" size="sm" variant="outline" @click="loadAttachments">{{ $t('ui.reload') }}</Button>
            </AlertDescription>
          </Alert>
          <ul v-else-if="attachmentDeliveryPolicy !== 'RandomOnePerTeam'" class="mt-3 divide-y border-y">
            <li
              v-for="attachment in attachments"
              :key="attachment.id"
              class="flex items-center justify-between gap-3 py-2.5"
            >
              <span class="flex min-w-0 items-center gap-2 text-sm">
                <FileDown class="size-4 shrink-0 text-muted-foreground" />
                <span class="truncate">{{ attachment.fileName }}</span>
                <span class="shrink-0 text-muted-foreground">{{ formatBytes(attachment.byteLength) }}</span>
              </span>
              <Button
                variant="ghost"
                size="sm"
                :disabled="downloading"
                @click="downloadAttachment(attachment.id!, attachment.fileName ?? 'attachment')"
              >
                <Download data-icon="inline-start" /> {{ $t('ui.download') }}
              </Button>
            </li>
          </ul>
      </section>

      <component :is="ChallengeHints"
        :key="`${competitionId}:${challenge.id}:${user?.userId ?? 'anonymous'}`"
        :competition-id="competitionId"
        :competition-challenge-id="competitionChallengeId"
        :hints="challenge.hints"
        @unlocked="refreshSubmissionHistory"
      />

      <div v-if="ctx.competition.value" class="pt-5">
        <component :is="CtfPanel"
          v-if="mode === 'Ctf'"
          :key="challenge.id"
          :competition="ctx.competition.value"
          :challenge="challenge"
          @submitted="refreshSubmissionHistory"
        />
        <component :is="AwdPanel"
          v-else-if="mode === 'Awd'"
          :key="challenge.id"
          :competition="ctx.competition.value"
          :challenge="challenge"
          @submitted="refreshSubmissionHistory"
        />
        <component :is="AwdpPanel"
          v-else-if="mode === 'Awdp'"
          :key="challenge.id"
          :competition="ctx.competition.value"
          :challenge="challenge"
          @submitted="refreshSubmissionHistory"
        />
        <component :is="KohPanel"
          v-else-if="mode === 'Koh'"
          :key="challenge.id"
          :competition="ctx.competition.value"
          :challenge="challenge"
        />
      </div>

      <component :is="ChallengeSubmissionHistory"
        v-if="isLoggedIn"
        :key="challenge.id"
        class="mt-5"
        :competition-id="competitionId"
        :competition-challenge-id="competitionChallengeId"
        :refresh-key="historyRefreshKey"
      />
    </div>
  </section>
</template>
