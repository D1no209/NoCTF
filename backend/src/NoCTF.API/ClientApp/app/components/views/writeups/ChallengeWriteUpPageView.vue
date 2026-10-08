<script setup lang="ts">
import { toRefs } from 'vue'
import type { ChallengeWriteUpPageState } from '~/features/writeups/useChallengeWriteUpPage'
const props = defineProps<{ state: ChallengeWriteUpPageState }>()
const { ArrowLeft, BookOpen, Download, RefreshCw, competitionId, challengeId, selectedId, selected, access, options, loading, reading,
  downloading, confirmPending, error, contentError, content, pdfUrl, pdfKey, quote, confirmOpen, search, mine, disabled, mode, title, own, retained, narrow, benefit,
  load, select, read, confirm, setConfirmOpen, download, back, openMine, openPublic, CompetitionParticipantWorkspace, Editor } = toRefs(props.state)
</script>
<template>
  <component :is="CompetitionParticipantWorkspace" :competition-id="competitionId" :content-scroll="mine" class="flex min-h-0 flex-1 flex-col">
    <div class="flex min-h-0 flex-1 flex-col gap-4" :class="mine ? '' : 'h-full'">
      <header class="flex flex-wrap items-center gap-3"><Button variant="ghost" size="sm" @click="back"><component :is="ArrowLeft" />{{ $t('challengeWriteUp.back') }}</Button><h1 class="min-w-0 flex-1 truncate text-xl font-semibold">{{ title || $t('challengeWriteUp.title') }}</h1><Button variant="ghost" size="icon-sm" :aria-label="$t('common.label.refresh')" @click="load"><component :is="RefreshCw" /></Button></header>
      <div class="flex flex-wrap gap-2"><Button :variant="mine ? 'ghost' : 'secondary'" @click="openPublic">{{ $t('challengeWriteUp.public') }}</Button><Button :variant="mine ? 'secondary' : 'ghost'" @click="openMine">{{ $t('challengeWriteUp.mine') }}</Button></div>
      <Card v-if="disabled"><CardContent><Empty><EmptyHeader><EmptyTitle>{{ $t('challengeWriteUp.disabled') }}</EmptyTitle></EmptyHeader></Empty></CardContent></Card>
      <Card v-else-if="mine"><CardContent><component :is="Editor" :key="challengeId" :competition-id="competitionId" :competition-challenge-id="challengeId" @saved="load" /></CardContent></Card>
      <div v-else class="grid min-h-0 flex-1 gap-6 lg:grid-cols-[14rem_minmax(0,1fr)]">
        <ChoicePicker v-if="narrow" :items="options" :model-value="selectedId || null" :label="$t('challengeWriteUp.public')" :search-label="$t('challengeWriteUp.search')" :empty-label="$t('challengeWriteUp.empty')" :disabled="loading" @update:model-value="select" /><ChoiceSidebar v-else contained :items="options" :model-value="selectedId || null" :loading="loading" :label="$t('challengeWriteUp.public')" :loading-label="$t('challengeWriteUp.public')" :empty-label="$t('challengeWriteUp.empty')" @update:model-value="select">
          <template #header><Input v-model="search" :placeholder="$t('challengeWriteUp.search')" :aria-label="$t('challengeWriteUp.search')" /></template>
          <template #item="{ item }"><span class="flex min-w-0 flex-col gap-1"><span class="truncate font-medium">{{ item.label }}</span><span class="text-xs text-muted-foreground">{{ item.row.published?.format }} · {{ formatDateTime(item.row.published?.publishedAt) }}</span></span></template>
        </ChoiceSidebar>
        <Card class="flex min-h-0 min-w-0 flex-col">
          <CardHeader v-if="selected"><div class="flex flex-wrap items-start justify-between gap-3"><div class="space-y-1"><CardTitle>{{ selected.source === 'Official' ? $t('challengeWriteUp.official') : selected.authorName }}</CardTitle><p class="text-sm text-muted-foreground">{{ $t('challengeWriteUp.version', { number: selected.published?.number ?? 1 }) }} · {{ formatDateTime(selected.published?.publishedAt) }}</p></div><Button v-if="content?.format === 'Pdf'" variant="outline" :disabled="downloading" @click="download"><component :is="Download" />{{ $t('writeups.label.download') }}</Button></div></CardHeader>
          <CardContent class="flex min-h-0 flex-1 flex-col gap-4">
            <Alert v-if="error || contentError" variant="destructive"><AlertDescription>{{ $message(error || contentError) }}</AlertDescription></Alert>
            <Skeleton v-if="loading || reading" class="h-72 w-full" />
            <template v-else-if="selected">
              <p v-if="access?.isFree" class="text-sm text-muted-foreground">{{ $t('challengeWriteUp.free') }} <span v-if="access.isUnlocked">{{ $t('challengeWriteUp.historyPreserved') }}</span></p>
              <p v-else-if="own" class="text-sm text-muted-foreground">{{ $t('challengeWriteUp.ownFree') }}</p>
              <p v-else-if="access?.isUnlocked" class="text-sm text-muted-foreground">{{ $t('challengeWriteUp.unlocked', { percent: retained }) }}</p>
              <ScoreBreakdownSummary v-if="benefit" :gross-points="benefit.grossPoints ?? 0" :deduction-points="benefit.writeUpDeductionPoints ?? 0" :net-points="benefit.netPoints ?? 0" :gross-label="$t('challengeWriteUp.grossLabel')" :deduction-label="$t('challengeWriteUp.deductionLabel')" :net-label="$t('challengeWriteUp.netLabel')" />
              <ScrollSurface v-if="content?.format === 'Markdown'" axis="both" class="min-h-0 flex-1"><MarkdownContent :source="content.markdown ?? ''" class="mx-auto max-w-[75ch] py-3 leading-7" /></ScrollSurface>
              <PdfPreview v-else-if="content?.format === 'Pdf'" :key="pdfKey" fill retryable @retry="read" :source="pdfUrl" :error="contentError" :accessible-label="$t('challengeWriteUp.preview')" :empty-label="$t('challengeWriteUp.previewEmpty')" />
              <Empty v-else><EmptyHeader><EmptyMedia><component :is="BookOpen" /></EmptyMedia><EmptyTitle>{{ $t('challengeWriteUp.select') }}</EmptyTitle><EmptyDescription v-if="!own && !access?.isFree && !access?.isUnlocked">{{ $t('challengeWriteUp.deduction', { percent: access?.deductionPercent ?? 20, retained }) }}</EmptyDescription></EmptyHeader><EmptyContent><Button :disabled="reading" @click="read">{{ $t('challengeWriteUp.read') }}</Button></EmptyContent></Empty>
              <Button v-if="contentError && !reading" variant="outline" @click="read">{{ $t('common.label.retry') }}</Button>
            </template>
            <Empty v-else><EmptyHeader><EmptyTitle>{{ $t('challengeWriteUp.empty') }}</EmptyTitle><EmptyDescription>{{ $t('challengeWriteUp.emptyDescription') }}</EmptyDescription></EmptyHeader><EmptyContent><Button @click="openMine">{{ $t('challengeWriteUp.write') }}</Button></EmptyContent></Empty>
          </CardContent>
        </Card>
      </div>
    </div>
    <AlertDialog :open="confirmOpen" @update:open="setConfirmOpen"><AlertDialogContent><AlertDialogHeader><AlertDialogTitle>{{ $t('challengeWriteUp.confirmTitle') }}</AlertDialogTitle><AlertDialogDescription>{{ $t('challengeWriteUp.deduction', { percent: quote?.deductionPercent ?? 20, retained: 100 - (quote?.deductionPercent ?? 20) }) }}</AlertDialogDescription></AlertDialogHeader>
      <div class="space-y-3 text-sm"><dl v-if="quote?.grossPoints != null" class="grid grid-cols-2 gap-2"><dt>{{ $t('challengeWriteUp.gross') }}</dt><dd class="text-right tabular-nums">{{ $t('challengeWriteUp.score', { points: quote.grossPoints }) }}</dd><dt>{{ $t('challengeWriteUp.estimated') }}</dt><dd class="text-right font-semibold tabular-nums">{{ $t('challengeWriteUp.score', { points: quote.estimatedDeductionPoints ?? 0 }) }}</dd></dl><p v-else>{{ $t('challengeWriteUp.hiddenEstimate') }}</p>
      <p v-if="quote?.grossPoints === 0">{{ $t('challengeWriteUp.noPoints', { retained: 100 - (quote.deductionPercent ?? 20) }) }}</p><p>{{ $t('challengeWriteUp.pastFuture') }}</p><p>{{ $t('challengeWriteUp.shared') }}</p><p v-if="mode === 'Ctf'" class="font-medium text-warning">{{ $t('challengeWriteUp.bloodWarning') }}</p><p class="text-muted-foreground">{{ $t('challengeWriteUp.dynamic') }}</p></div>
      <AlertDialogFooter><AlertDialogCancel :disabled="confirmPending">{{ $t('common.action.cancel') }}</AlertDialogCancel><Button :disabled="confirmPending" @click="confirm"><Spinner v-if="confirmPending" />{{ $t('challengeWriteUp.confirm') }}</Button></AlertDialogFooter>
    </AlertDialogContent></AlertDialog>
  </component>
</template>
