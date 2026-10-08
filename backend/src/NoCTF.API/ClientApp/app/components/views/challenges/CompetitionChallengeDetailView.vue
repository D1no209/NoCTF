<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionChallengeDetailViewState } from '~/features/challenges/useCompetitionChallengeDetail'

const viewProps = defineProps<{ state: CompetitionChallengeDetailViewState }>()
const { BookOpen, singleWriteUpsEnabled, openWriteUps, writeUpBenefit, Dice5, FileDown, History, ctx, isLoggedIn, user, challenge, loading, error, attachments, attachmentDeliveryPolicy, attachmentsLoading, attachmentError, downloading, historyRefreshKey, historyOpen, refreshSubmissionHistory, updateRemainingAttempts, loadAttachments, downloadAttachment, downloadRandom, mode, ChallengeHints, ChallengeSubmissionHistory, AwdPanel, AwdpPanel, CtfPanel, KohPanel, competitionId, competitionChallengeId, flagDockTarget } = toRefs(viewProps.state)
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
      <header class="relative isolate flex min-h-28 flex-wrap items-center gap-3 overflow-hidden pb-5">
        <TypeWatermark :text="challenge.direction || ''" :class="directionWatermarkClass(challenge.direction)" />
        <LucideIcon v-if="challenge.directionIcon" :name="challenge.directionIcon" class="relative z-10" :class="directionTextClass(challenge.direction)" />
        <h2 class="relative z-10 font-sans text-2xl font-bold italic">{{ challenge.title }}</h2>
        <RemainingAttempts
          v-if="isLoggedIn && challenge.remainingFlagAttempts !== null && challenge.remainingFlagAttempts !== undefined"
          :count="challenge.remainingFlagAttempts"
          class="relative z-10 font-sans text-lg font-bold italic tabular-nums text-primary"
        />
        <Dialog v-if="isLoggedIn" v-model:open="historyOpen">
          <Hint :content="$t('challenges.label.challengeSubmissionHistory')">
            <DialogTrigger as-child>
              <Button variant="ghost" size="icon-sm" class="relative z-10" :aria-label="$t('challenges.label.challengeSubmissionHistory')"><History /></Button>
            </DialogTrigger>
          </Hint>
          <DialogContent class="sm:max-w-3xl">
            <DialogHeader class="sr-only"><DialogTitle>{{ $t('challenges.label.challengeSubmissionHistory') }}</DialogTitle><DialogDescription>{{ $t('challenges.challengeSubmission.description.teamMembersViewFlag') }}</DialogDescription></DialogHeader>
            <ScrollSurface axis="y" class="max-h-[65dvh]" :aria-label="$t('challenges.label.challengeSubmissionHistory')">
              <component :is="ChallengeSubmissionHistory" v-if="historyOpen" :key="challenge.id" :competition-id="competitionId" :competition-challenge-id="competitionChallengeId" :refresh-key="historyRefreshKey" />
            </ScrollSurface>
          </DialogContent>
        </Dialog>
        <Button v-if="singleWriteUpsEnabled" variant="ghost" size="sm" @click="openWriteUps"><BookOpen />{{ $t('challengeWriteUp.title') }}</Button>
        <span class="sr-only">{{ challenge.direction || '' }}</span>
        <Badge v-if="mode === 'Awdp'" variant="secondary">{{ $t('challenges.label.scoresSettleRound') }}</Badge>
        <div v-if="challenge.tags?.length" class="relative z-10 flex min-w-0 basis-full flex-wrap gap-2" :aria-label="$t('challengeTags.label')">
          <Badge v-for="tag in challenge.tags" :key="tag" variant="secondary" class="min-w-0 max-w-full wrap-anywhere whitespace-normal px-3 py-1 text-sm leading-5">{{ tag }}</Badge>
        </div>
      </header>

      <div v-if="writeUpBenefit" class="space-y-3 border-b py-4"><Badge variant="secondary">{{ $t('challengeWriteUp.unlocked', { percent: 100 - (writeUpBenefit.writeUpDeductionPercent ?? 0) }) }}</Badge><ScoreBreakdownSummary :gross-points="writeUpBenefit.grossPoints ?? 0" :deduction-points="writeUpBenefit.writeUpDeductionPoints ?? 0" :net-points="writeUpBenefit.netPoints ?? 0" :gross-label="$t('challengeWriteUp.grossLabel')" :deduction-label="$t('challengeWriteUp.deductionLabel')" :net-label="$t('challengeWriteUp.netLabel')" /></div>
      <section v-if="challenge.description" class="border-b py-5" :aria-label="challenge.title">
        <MarkdownContent :source="challenge.description" class="text-foreground/90" />
      </section>

      <div data-slot="challenge-resource-row">
      <div id="challenge-runtime-dock" data-slot="challenge-runtime-dock" />
      <section
        v-if="isLoggedIn && (attachmentsLoading || attachmentError || attachments.length || attachmentDeliveryPolicy === 'RandomOnePerTeam')"
        data-slot="challenge-attachments"
        class="min-w-0 py-5"
        aria-labelledby="challenge-attachments-title"
      >
        <div class="flex items-center justify-between gap-3">
            <h3 id="challenge-attachments-title" class="text-sm font-semibold">{{ $t('common.label.accessories') }}</h3>
            <Button
              v-if="!attachmentsLoading && !attachmentError && attachmentDeliveryPolicy === 'RandomOnePerTeam'"
              variant="outline"
              size="sm"
              :disabled="downloading"
              @click="downloadRandom"
            >
              <Dice5 data-icon="inline-start" /> {{ $t('challenges.label.downloadAttachment') }}
            </Button>
        </div>
          <Skeleton v-if="attachmentsLoading" class="mt-3 h-12 w-full" />
          <Alert v-else-if="attachmentError" variant="destructive" class="mt-3">
            <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
              <span>{{ $message(attachmentError) }}</span>
              <Button type="button" size="sm" variant="outline" @click="loadAttachments">{{ $t('common.label.reload') }}</Button>
            </AlertDescription>
          </Alert>
          <ul v-else-if="attachmentDeliveryPolicy !== 'RandomOnePerTeam'" class="mt-3 flex flex-wrap gap-2">
            <li
              v-for="attachment in attachments"
              :key="attachment.id"
              class="min-w-0"
            >
              <Button
                variant="outline"
                size="sm"
                class="max-w-full"
                :disabled="downloading"
                @click="downloadAttachment(attachment.id!, attachment.fileName ?? 'attachment')"
              >
                <FileDown data-icon="inline-start" /> <span class="truncate">{{ attachment.fileName }}</span>
              </Button>
            </li>
          </ul>
      </section>
      </div>

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
          :flag-dock-target="flagDockTarget"
          runtime-dock-target="#challenge-runtime-dock"
          @submitted="refreshSubmissionHistory"
          @remaining-changed="updateRemainingAttempts"
        />
        <component :is="AwdPanel"
          v-else-if="mode === 'Awd'"
          :key="challenge.id"
          :competition="ctx.competition.value"
          :challenge="challenge"
          :flag-dock-target="flagDockTarget"
          runtime-dock-target="#challenge-runtime-dock"
          @submitted="refreshSubmissionHistory"
          @remaining-changed="updateRemainingAttempts"
        />
        <component :is="AwdpPanel"
          v-else-if="mode === 'Awdp'"
          :key="challenge.id"
          :competition="ctx.competition.value"
          :challenge="challenge"
          :flag-dock-target="flagDockTarget"
          runtime-dock-target="#challenge-runtime-dock"
          @submitted="refreshSubmissionHistory"
          @remaining-changed="updateRemainingAttempts"
        />
        <component :is="KohPanel"
          v-else-if="mode === 'Koh'"
          :key="challenge.id"
          :competition="ctx.competition.value"
          :challenge="challenge"
        />
      </div>

    </div>
  </section>
</template>
