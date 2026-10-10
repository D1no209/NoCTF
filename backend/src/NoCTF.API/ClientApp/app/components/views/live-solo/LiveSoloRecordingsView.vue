<script setup lang="ts">
import { toRefs } from 'vue'
import type { LiveSoloRecordingsState } from '~/features/live-solo/useLiveSoloRecordings'
const props = defineProps<{ state: LiveSoloRecordingsState }>()
const { options, selectedId, current, source, historyRows, actions, failureKey, playable, staff, error, mediaError, busy, dialog, reason, actionKey, decision,
  loading, listError, pageNumber, pageCount, total, limit, select, openReplacement, load, download, begin, setDialog, confirm, retry, failed, page, pageSize, back } = toRefs(props.state)
</script>
<template>
  <div data-contained-workspace-page class="flex h-full min-h-0 flex-col gap-4 px-4 pb-6 pt-3 md:px-8">
    <header class="flex flex-wrap items-center gap-3"><Button variant="ghost" @click="back">{{ $t('liveSolo.back') }}</Button><h1 class="text-display text-2xl">{{ $t(staff ? 'liveSolo.recording.staffTitle' : 'liveSolo.recording.replays') }}</h1><Button class="ml-auto" variant="ghost" :disabled="loading" @click="load">{{ $t('common.label.refresh') }}</Button></header>
    <Alert v-if="error || mediaError || listError" variant="destructive"><AlertDescription>{{ $message(error || mediaError || listError?.displayMessage) }}</AlertDescription></Alert>
    <div class="grid min-h-0 flex-1 gap-5 lg:grid-cols-[17rem_minmax(0,1fr)]">
      <ChoiceSidebar contained :items="options" :model-value="selectedId" :loading="loading" :label="$t('liveSolo.recording.staffTitle')" :loading-label="$t('liveSolo.recording.staffTitle')" :empty-label="$t('liveSolo.recording.empty')" @update:model-value="select">
        <template #item="{ item }"><span class="flex min-w-0 flex-col gap-1"><span class="truncate font-semibold">{{ item.row.userName }}</span><span class="truncate text-sm text-muted-foreground">{{ item.row.teamName }}</span><span class="text-xs text-muted-foreground">{{ formatDateTime(item.row.startedAt || item.row.createdAt) }}</span></span></template>
        <template #footer><OffsetPagination :page="pageNumber" :page-count="pageCount" :total="total" :limit="limit" :loading="loading" @update:page="page" @update:limit="pageSize" /></template>
      </ChoiceSidebar>
      <Card class="flex min-h-0 flex-col"><ScrollSurface axis="y" class="h-full" :aria-label="$t('liveSolo.recording.staffTitle')"><CardContent class="flex flex-col gap-5 py-5">
        <template v-if="current"><div class="flex flex-wrap items-center gap-3"><h2 class="min-w-0 break-words text-xl font-semibold">{{ current.teamName }} · {{ current.userName }}</h2><Badge v-if="current.published">{{ $t('liveSolo.recording.published') }}</Badge><Badge v-if="current.disputeHold" variant="secondary">{{ $t('liveSolo.recording.onHold') }}</Badge><Badge v-if="!playable" variant="secondary">{{ $t('liveSolo.recording.processing') }}</Badge></div>
          <div class="flex flex-wrap gap-4 text-sm text-muted-foreground"><span>{{ formatDateTime(current.startedAt || current.createdAt) }} — {{ formatDateTime(current.endedAt) }}</span><span v-if="staff">{{ $t('liveSolo.recording.keepUntil', { time: formatDateTime(current.keepUntil) }) }}</span></div>
          <p v-if="failureKey" role="status" class="text-sm text-destructive">{{ $t(failureKey) }}</p>
          <p class="text-sm text-muted-foreground">{{ $t('liveSolo.recording.chunk', { number: (current.chunk ?? 0) + 1 }) }}</p>
          <VideoFilePlayer :key="current.id" :source="source" :label="$t('liveSolo.recording.preview')" :empty-label="$t('liveSolo.recording.unavailable')" :failed-label="$t('liveSolo.program.failed')" :retry-label="$t('common.label.retry')" @retry="retry" @failed="failed" />
          <div class="flex flex-wrap gap-3"><Button :disabled="!playable || busy" @click="download">{{ $t('liveSolo.recording.download') }}</Button><Button v-for="item in actions" :key="item.action" variant="outline" :disabled="busy" @click="begin(item.action)">{{ $t(item.key) }}</Button></div>
          <template v-if="staff"><Separator /><h3 class="font-semibold">{{ $t('liveSolo.recording.history') }}</h3><div v-for="row in historyRows" :key="row.entry.id" class="flex flex-col gap-1 py-2"><span class="text-sm text-muted-foreground">{{ formatDateTime(row.entry.occurredAt) }} · {{ $t(row.actionKey) }}</span><p class="whitespace-pre-wrap break-words">{{ row.entry.reason }}</p><Button v-if="row.entry.replacementRecordingId" variant="link" class="self-start p-0" @click="openReplacement(row.entry.replacementRecordingId)">{{ $t('liveSolo.recording.openReplacement') }}</Button></div></template>
        </template><Empty v-else><EmptyHeader><EmptyTitle>{{ $t('liveSolo.recording.empty') }}</EmptyTitle></EmptyHeader></Empty>
      </CardContent></ScrollSurface></Card>
    </div>
    <AlertDialog :open="dialog" @update:open="setDialog"><AlertDialogContent><AlertDialogHeader><AlertDialogTitle>{{ $t(actionKey) }}</AlertDialogTitle><AlertDialogDescription>{{ $t('liveSolo.recording.confirm') }}</AlertDialogDescription></AlertDialogHeader><p>{{ decision?.row.teamName }} · {{ decision?.row.userName }} · {{ formatDateTime(decision?.row.startedAt) }}</p><Alert v-if="decision?.action === 'Publish'"><AlertDescription>{{ $t('liveSolo.recording.publishWarning') }}</AlertDescription></Alert><Field><FieldLabel for="recording-decision-reason">{{ $t('liveSolo.judge.reason') }}</FieldLabel><Textarea id="recording-decision-reason" v-model="reason" :disabled="busy" :maxlength="4000" /></Field><AlertDialogFooter><AlertDialogCancel :disabled="busy">{{ $t('common.action.cancel') }}</AlertDialogCancel><Button :disabled="busy || !reason.trim()" @click="confirm">{{ $t(actionKey) }}</Button></AlertDialogFooter></AlertDialogContent></AlertDialog>
  </div>
</template>
