<script setup lang="ts">
import { toRefs } from 'vue'
import type { CompetitionsByIdQuestionsPageViewState } from '~/features/routes/competitions/[id]/useCompetitionsByIdQuestionsPage'

const viewProps = defineProps<{ state: CompetitionsByIdQuestionsPageViewState }>()
const { maximumQuestionBodyLength, maximumQuestionTitleLength, minimumQuestionBodyLength, minimumQuestionTitleLength, competitionId, isOwnMessage, unreadCount, questions, loading, hasMore, initialized, listError, loadMoreQuestions, createOpen, createSubject, createChallengeId, createTitle, createBody, createPending, createError, challenges, challengesLoading, challengeLoadError, loadChallengeOptions, submitCreate, setCreateOpen, selectedId, detail, detailLoading, select, reply, replyPending, replyError, submitReply, statusPending, changeStatus, statusVariant, statusLabel, roleLabel, isHandlerRole, participantLimitReached, CompetitionParticipantWorkspace, onInputCreateError, onInputReplyError } = toRefs(viewProps.state)
</script>

<template>
  <component :is="CompetitionParticipantWorkspace" :competition-id="competitionId" :content-scroll="false" class="question-participant-workspace">
    <div class="questions-chat-layout">
    <div class="flex min-h-0 min-w-0 flex-col gap-4">
      <div class="flex items-center justify-between">
        <h2 class="text-lg font-semibold">{{ $t('ui.consultationQA') }}</h2>
        <Dialog :open="createOpen" @update:open="setCreateOpen">
          <DialogTrigger as-child>
            <Button size="sm">{{ $t('ui.initiateConsultation') }}</Button>
          </DialogTrigger>
          <DialogContent data-scroll-surface class="max-h-[calc(100vh-2rem)] overflow-y-auto sm:max-w-2xl">
            <DialogHeader>
              <DialogTitle>{{ $t('ui.initiateConsultation') }}</DialogTitle>
              <DialogDescription>{{ $t('ui.byDefaultConsultationContentIsOnlyVisibleToTheTeam') }}</DialogDescription>
            </DialogHeader>
            <UiForm @submit.prevent>
              <FieldGroup>
                <Field>
                  <FieldLabel>{{ $t('ui.type') }}</FieldLabel>
                  <Select v-model="createSubject">
                    <SelectTrigger><SelectValue /></SelectTrigger>
                    <SelectContent>
                      <SelectGroup>
                        <SelectItem value="Challenge">{{ $t('ui.topicRelated') }}</SelectItem>
                        <SelectItem value="Platform">{{ $t('ui.platformEventRelated') }}</SelectItem>
                      </SelectGroup>
                    </SelectContent>
                  </Select>
                </Field>
                <Field v-if="createSubject === 'Challenge'">
                  <FieldLabel>{{ $t('ui.relatedQuestionsRequired') }}</FieldLabel>
                  <Skeleton v-if="challengesLoading" class="h-10 w-full" />
                  <Alert v-else-if="challengeLoadError" variant="destructive">
                    <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
                      <span>{{ $message(challengeLoadError) }}</span>
                      <Button type="button" size="sm" variant="outline" @click="loadChallengeOptions">{{ $t('ui.reload') }}</Button>
                    </AlertDescription>
                  </Alert>
                  <Select v-else v-model="createChallengeId">
                    <SelectTrigger><SelectValue :placeholder="$t('ui.notRelatedToTheTopic')" /></SelectTrigger>
                    <SelectContent>
                      <SelectGroup>
                        <SelectItem value="none">{{ $t('ui.notRelatedToTheTopic') }}</SelectItem>
                        <SelectItem v-for="c in challenges" :key="c.id" :value="c.id!">
                          {{ c.title }}
                        </SelectItem>
                      </SelectGroup>
                    </SelectContent>
                  </Select>
                </Field>
                <Field>
                  <FieldLabel for="q-title">{{ $t('ui.title') }}</FieldLabel>
                  <Input
                    id="q-title"
                    v-model="createTitle"
                    required
                    :minlength="minimumQuestionTitleLength"
                    :maxlength="maximumQuestionTitleLength"
                    @input="onInputCreateError(null)"
                  />
                </Field>
                <Field>
                  <FieldLabel for="q-body">{{ $t('ui.content') }}</FieldLabel>
                  <Textarea
                    id="q-body"
                    v-model="createBody"
                    class="min-h-44"
                    rows="10"
                    required
                    :minlength="minimumQuestionBodyLength"
                    :maxlength="maximumQuestionBodyLength"
                    @input="onInputCreateError(null)"
                  />
                </Field>
                <p v-if="createError" role="alert" class="text-sm text-destructive">
                  {{ $message(createError) }}
                </p>
                <Field>
                  <Button
                    type="button"
                    class="w-full"
                    :disabled="createPending || createSubject === 'Challenge' && (challengesLoading || Boolean(challengeLoadError))"
                    @click="submitCreate"
                  >
                    <Spinner v-if="createPending" data-icon="inline-start" /> {{ $t('ui.submissions') }} </Button>
                </Field>
              </FieldGroup>
            </UiForm>
          </DialogContent>
        </Dialog>
      </div>

      <ScrollSurface axis="y" class="min-h-0 flex-1" :aria-label="$t('ui.consultationQA')">
      <Alert v-if="listError" variant="destructive">
        <AlertDescription>{{ $message(listError) }}</AlertDescription>
      </Alert>
      <Skeleton v-if="loading && !initialized" class="h-40 w-full" />
      <Empty v-else-if="initialized && !questions.length" class="border py-8">
        <EmptyHeader>
          <EmptyTitle>{{ $t('ui.noConsultationYet') }}</EmptyTitle>
          <EmptyDescription>{{ $t('ui.encounterAProblemInitiateConsultationWithTheOrganizer') }}</EmptyDescription>
        </EmptyHeader>
      </Empty>
      <ul v-else-if="questions.length" class="flex flex-col gap-2">
        <li v-for="q in questions" :key="q.threadRootId">
          <ActionButton
            type="button"
            class="w-full rounded-md border px-3 py-2 text-left transition-colors hover:border-primary/50"
            :class="selectedId === q.threadRootId ? 'text-primary' : ''"
            @click="select(q.threadRootId!)"
          >
            <div class="flex items-center justify-between gap-2">
              <span class="min-w-0 truncate text-sm font-medium">{{ q.title }}</span>
              <div class="flex shrink-0 items-center gap-1.5">
                <Badge v-if="unreadCount(q) > 0" variant="destructive">
                  {{ $t('ui.unread', { count: unreadCount(q) }) }}
                </Badge>
                <Badge :variant="statusVariant(q.status)">{{ statusLabel(q.status) }}</Badge>
              </div>
            </div>
            <p class="mt-1 text-xs text-muted-foreground">
              {{ q.subject === 'Challenge' ? $t('ui.challenge2', { title: q.challengeTitle ?? $t('ui.unknownQuestion') }) : $t('ui.platformCompetition') }}
            </p>
            <p class="mt-1 truncate text-xs text-muted-foreground">
              {{ $t('ui.lastUpdatedBy', { role: roleLabel(q.lastActorRole), actor: q.lastActorDisplayName ?? '-', time: formatDateTime(q.updatedAt) }) }}
            </p>
          </ActionButton>
          <div v-if="selectedId === q.threadRootId && detail && !detailLoading" class="flex flex-wrap gap-2 px-3 pb-3">
            <Button v-if="detail.canResolve" size="sm" variant="outline" :disabled="statusPending" @click="changeStatus('Resolved')">{{ $t('ui.flagResolved') }}</Button>
            <Button v-if="detail.canClose" size="sm" variant="outline" :disabled="statusPending" @click="changeStatus('Closed')">{{ $t('ui.closeConsultation') }}</Button>
          </div>
        </li>
      </ul>
      <div v-if="!initialized && listError" class="flex justify-center">
        <Button variant="outline" :disabled="loading" @click="loadMoreQuestions">
          <Spinner v-if="loading" data-icon="inline-start" /> {{ $t('ui.reload') }} </Button>
      </div>
      <div v-if="hasMore" class="flex justify-center">
        <Button variant="outline" :disabled="loading" @click="loadMoreQuestions">
          <Spinner v-if="loading" data-icon="inline-start" /> {{ $t('ui.loadMore') }} </Button>
      </div>
      </ScrollSurface>
    </div>

    <div class="min-h-0 min-w-0">
      <Empty v-if="!selectedId" class="h-full">
        <EmptyHeader><EmptyTitle>{{ $t('ui.selectConsultationOnTheLeftToViewTheConversation') }}</EmptyTitle></EmptyHeader>
      </Empty>
      <Skeleton v-else-if="detailLoading" class="h-full w-full" />
      <ConversationPanel v-else-if="detail" :key="detail.threadRootId" :identity="detail.threadRootId" :item-count="detail.entries?.length" :history-label="$t('ui.consultationQA')">
        <template #header>
          <div class="flex items-center justify-between gap-2">
            <h2 class="text-base font-semibold">{{ detail.title }}</h2>
            <Badge :variant="statusVariant(detail.status)">{{ statusLabel(detail.status) }}</Badge>
          </div>
          <p class="mt-1 text-sm text-muted-foreground">
            {{ detail.subject === 'Challenge' ? $t('ui.challengeQuestion', { title: detail.challengeTitle ?? $t('ui.unknownQuestion') }) : $t('ui.platformEventConsultation') }}
            · {{ detail.teamDisplayName ?? detail.askerDisplayName }}
          </p>
        </template>
        <template #history>
          <template v-for="entry in detail.entries ?? []" :key="entry.id">
            <MessageBubble v-if="entry.kind === 'Message'" :own="isOwnMessage(entry.actorUserId)">
              <template #meta>
                <Badge :variant="isHandlerRole(entry.actorRole) ? 'default' : 'secondary'">{{ roleLabel(entry.actorRole) }}</Badge>
                <span>{{ entry.actorDisplayName }}</span>
                <span>{{ formatDateTime(entry.createdAt) }}</span>
              </template>
              {{ entry.body }}
            </MessageBubble>
            <li v-else-if="entry.kind === 'StatusTransition'" class="self-center text-center text-xs leading-relaxed text-muted-foreground">
              {{ $t('ui.statusChanged', { from: statusLabel(entry.fromStatus ?? undefined) ?? '-', to: statusLabel(entry.toStatus ?? undefined) ?? '-' }) }}
              · {{ roleLabel(entry.actorRole) }} {{ entry.actorDisplayName }}
              · {{ formatDateTime(entry.createdAt) }}
            </li>
          </template>
        </template>
        <template #composer>
          <UiForm v-if="detail.canReply" validation="feature" class="flex h-full min-h-0 flex-col gap-2" @submit.prevent="submitReply">
            <Label for="q-reply" class="sr-only">{{ $t('ui.addMessage') }}</Label>
            <ScrollSurface axis="y" class="min-h-0 flex-1" :aria-label="$t('ui.addMessage')">
              <Textarea id="q-reply" v-model="reply" class="min-h-full w-full" :maxlength="maximumQuestionBodyLength" :disabled="replyPending" required @input="onInputReplyError(null)" />
            </ScrollSurface>
            <Alert v-if="replyError" variant="destructive"><AlertDescription>{{ $message(replyError) }}</AlertDescription></Alert>
            <div class="flex shrink-0 items-end justify-between gap-4">
              <p v-if="detail.access === 'Asker'" class="text-xs leading-relaxed text-muted-foreground">
                {{ $t('ui.youCanSendOfConsecutiveMessagesTheAllowanceResetsAfter', { remaining: detail.participantMessagesRemaining ?? 0, maximum: detail.maxParticipantMessagesBeforeHandlerReply ?? 3 }) }}
                <span v-if="detail.status === 'Resolved'">{{ $t('ui.continuingToAskWillResetTheInquiryToPending') }}</span>
              </p>
              <p v-else class="text-xs text-muted-foreground">{{ $t('ui.staffRepliesAreNotSubjectToTheContinuousMessageQuota') }}</p>
              <Button type="submit" class="ml-auto shrink-0" :disabled="replyPending || !reply.trim()">
                <Spinner v-if="replyPending" data-icon="inline-start" />{{ $t('ui.send') }}
              </Button>
            </div>
          </UiForm>
          <p v-else-if="participantLimitReached" class="text-sm text-muted-foreground">{{ $t('ui.youCanSendUpToConsecutiveMessagesBeforeAStaff2', { maximum: detail.maxParticipantMessagesBeforeHandlerReply ?? 3 }) }}</p>
          <p v-else-if="detail.access === 'Observer'" class="text-sm text-muted-foreground">{{ $t('ui.youHaveReadOnlyAccessToThisInquiry') }}</p>
          <p v-else class="text-sm text-muted-foreground">{{ $t('ui.thisQuestionIsAndCannotReceiveFurtherReplies', { status: statusLabel(detail.status) ?? '-' }) }}</p>
        </template>
      </ConversationPanel>
    </div>
    </div>
  </component>
</template>
