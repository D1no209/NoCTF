<script setup lang="ts">
import { toRefs } from 'vue'
import type { ChallengeHintsViewState } from '~/features/challenges/useChallengeHints'

const viewProps = defineProps<{ state: ChallengeHintsViewState }>()
const { Lightbulb, LockKeyhole, readableHintContent, isLoggedIn, headingId, hints, refreshing, refreshError, unlockError, confirmingId, submitting, pendingFactId, busy, pollingError, timedOut, unlock, retry, onClickConfirmingId, onClickConfirmingId2 } = toRefs(viewProps.state)
</script>

<template>
  <section class="py-5" :aria-labelledby="headingId" aria-live="polite">
    <div class="flex items-center justify-between gap-3">
      <h3 :id="headingId" class="flex items-center gap-2 text-sm font-semibold">
        <Lightbulb class="size-4 text-primary" />{{ $t('challenges.label.challengeHints') }}
      </h3>
      <Button type="button" variant="ghost" size="sm" :disabled="refreshing" @click="retry">
        <Spinner v-if="refreshing" data-icon="inline-start" />{{ $t('challenges.label.refreshHints') }}
      </Button>
    </div>
    <Alert v-if="refreshError || unlockError || pollingError || timedOut" variant="destructive" class="mt-3">
      <AlertDescription class="flex flex-wrap items-center justify-between gap-3">
        <span>{{ refreshError ?? unlockError ?? (timedOut ? $t('challenges.challengeHints.description.hintUnlockCompletedReload') : $t('challenges.challengeHints.error.hintUnlockStatusFailed')) }}</span>
        <Button type="button" variant="outline" size="sm" @click="retry">{{ $t('common.label.reload') }}</Button>
      </AlertDescription>
    </Alert>
    <p v-else-if="hints.length === 0" class="mt-3 text-sm text-muted-foreground">{{ $t('challenges.label.publishedHintsYet') }}</p>
    <p v-if="pendingFactId && !timedOut" class="mt-3 flex items-center gap-2 text-sm text-muted-foreground">
      <Spinner class="size-4" />{{ $t('challenges.label.unlockingHint') }}
    </p>
    <ul v-if="hints.length" class="mt-2">
      <li v-for="(hint, index) in hints" :key="hint.id ?? index" class="py-3">
        <div class="flex flex-wrap items-center justify-between gap-2">
          <span class="text-sm font-medium">{{ $t('challenges.label.hint', { index: index + 1 }) }}</span>
          <Badge v-if="hint.isUnlocked && (hint.cost ?? 0) > 0" variant="secondary">{{ $t('common.label.unlocked') }}</Badge>
          <Button v-else-if="!hint.isUnlocked" type="button" variant="outline" size="sm" :disabled="!hint.canUnlock || busy" @click="onClickConfirmingId(hint.id ?? null)">
            <LockKeyhole data-icon="inline-start" />{{ $t('challenges.label.unlockHintPoints', { cost: hint.cost ?? 0 }) }}
          </Button>
        </div>
        <MarkdownQuote v-if="readableHintContent(hint) !== null" :source="readableHintContent(hint) ?? ''" class="mt-2" />
        <p v-else-if="!hint.canUnlock" class="mt-2 text-sm text-muted-foreground">
          {{ isLoggedIn ? $t('challenges.challengeHints.validation.hintsUnlockedFormat') : $t('challenges.challengeHints.label.signUnlockHints') }}
        </p>
        <div v-if="confirmingId === hint.id && !pendingFactId" class="mt-3 flex flex-wrap items-center gap-3">
          <p class="text-sm">{{ $t('challenges.challengeHints.description.unlockingHintDeductsPoints', { cost: hint.cost ?? 0 }) }}</p>
          <Button type="button" size="sm" :disabled="busy" @click="unlock(hint)">
            <Spinner v-if="submitting" data-icon="inline-start" />{{ $t('challenges.label.confirmUnlock') }}
          </Button>
          <Button type="button" variant="ghost" size="sm" :disabled="busy" @click="onClickConfirmingId2(null)">{{ $t('common.action.cancel') }}</Button>
        </div>
        <Separator v-if="index < hints.length - 1" class="mt-4" />
      </li>
    </ul>
  </section>
  <Separator />
</template>
