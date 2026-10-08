<script setup lang="ts">
import { toRefs } from 'vue'
import type { ChallengeWriteUpEditorState } from '~/features/writeups/useChallengeWriteUpEditor'
const props = defineProps<{ state: ChallengeWriteUpEditorState }>()
const { root, loading, pending, dirty, error, markdown, format, pdfUrl, fileName, uploadKey, leaveOpen, canSave, canSubmit, currentVersion,
  statusKey, deadlineAt, selectFile, save, submit, reload, stay, discard, setLeaveOpen, official, access,
  historyId, historyContent, historyPdf, historyLoading, historyVersion, historyOptions, viewHistory,
  publishOpen, canPublish, requestPublish, setPublishOpen, publish } = toRefs(props.state)
</script>
<template>
  <section class="min-w-0 space-y-4">
    <header class="flex flex-wrap items-start justify-between gap-3">
      <div class="space-y-1"><h2 class="text-lg font-semibold">{{ official ? $t('challengeWriteUp.officialEditor') : $t('challengeWriteUp.mine') }}</h2>
        <p v-if="deadlineAt && !official" class="text-sm text-muted-foreground">{{ $t('challengeWriteUp.deadline', { time: formatDateTime(deadlineAt) }) }}</p></div>
      <div class="flex flex-wrap items-center gap-2"><Badge>{{ $t(statusKey) }}</Badge><Badge v-if="currentVersion" variant="secondary">{{ $t('challengeWriteUp.version', { number: currentVersion.number ?? 1 }) }}</Badge><Badge v-if="dirty" variant="secondary">{{ $t('challengeWriteUp.dirty') }}</Badge></div>
    </header>
    <Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert>
    <Field v-if="historyOptions.length"><FieldLabel>{{ $t('challengeWriteUp.history') }}</FieldLabel><Select :model-value="historyId || 'current'" :disabled="pending" @update:model-value="viewHistory"><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value="current">{{ $t('challengeWriteUp.currentEditor') }}</SelectItem><SelectItem v-for="version in historyOptions" :key="version.id" :value="version.id!">{{ $t('challengeWriteUp.version', { number: version.number ?? 1 }) }} · {{ formatDateTime(version.submittedAt ?? version.updatedAt) }}</SelectItem></SelectContent></Select></Field>
    <Skeleton v-if="loading" class="h-80 w-full" />
    <section v-else-if="historyId" class="space-y-3" :aria-label="$t('challengeWriteUp.history')"><p class="text-sm text-muted-foreground">{{ $t('challengeWriteUp.historyReadOnly') }}</p><p v-if="historyVersion" class="text-sm">{{ $t('challengeWriteUp.reviewedBy', { name: historyVersion.actorDisplayName ?? $t('challengeWriteUp.deletedUser'), time: formatDateTime(historyVersion.submittedAt ?? historyVersion.updatedAt) }) }}</p><p v-if="historyVersion?.reviewReason" class="text-sm">{{ historyVersion.reviewReason }}</p><Skeleton v-if="historyLoading" class="h-64" /><ScrollSurface v-else-if="historyContent?.format === 'Markdown'" axis="both" class="max-h-[60dvh]"><MarkdownContent :source="historyContent.markdown ?? ''" class="max-w-[75ch]" /></ScrollSurface><PdfPreview v-else-if="historyContent?.format === 'Pdf'" :source="historyPdf" :accessible-label="$t('challengeWriteUp.history')" :empty-label="$t('challengeWriteUp.previewEmpty')" /></section>
    <UiForm v-else validation="feature" class="space-y-4" @submit.prevent="save">
      <FieldDescription v-if="!canSave && !official">{{ $t('challengeWriteUp.closed') }}</FieldDescription>
      <FieldDescription v-if="root?.submitted?.reviewReason">{{ root.submitted.reviewReason }}</FieldDescription>
      <Field><FieldLabel>{{ $t('challengeWriteUp.format') }}</FieldLabel><Select v-model="format" :disabled="!canSave || pending"><SelectTrigger><SelectValue /></SelectTrigger><SelectContent><SelectItem value="Markdown">{{ $t('challengeWriteUp.markdown') }}</SelectItem><SelectItem value="Pdf">{{ $t('challengeWriteUp.pdf') }}</SelectItem></SelectContent></Select></Field>
      <MarkdownEditor v-if="format === 'Markdown'" v-model="markdown" :disabled="!canSave || pending" :label="$t('challengeWriteUp.mine')" :placeholder="$t('challengeWriteUp.markdownPlaceholder')" @save="save" />
      <div v-else class="space-y-3">
        <FileUpload v-if="canSave" :key="uploadKey" accept="application/pdf,.pdf" :pending="pending" :error="error" @change="selectFile" />
        <FieldDescription>{{ $t('challengeWriteUp.pdfRequirement') }}</FieldDescription>
        <p v-if="fileName" class="break-words text-sm">{{ fileName }}</p>
        <PdfPreview v-if="pdfUrl" :source="pdfUrl" :accessible-label="$t('challengeWriteUp.preview')" :empty-label="$t('challengeWriteUp.previewEmpty')" />
      </div>
      <div class="flex flex-wrap items-center justify-between gap-3">
        <Button type="button" variant="ghost" :disabled="pending" @click="reload">{{ $t('challengeWriteUp.reload') }}</Button>
        <div v-if="canSave" class="flex flex-wrap gap-2"><Button type="submit" variant="outline" :disabled="pending"><Spinner v-if="pending" />{{ $t('challengeWriteUp.save') }}</Button><Button type="button" :disabled="!canSubmit" @click="submit">{{ $t('challengeWriteUp.submit') }}</Button><Button v-if="official" type="button" :disabled="!canPublish" @click="requestPublish">{{ $t('challengeWriteUp.publish') }}</Button></div>
      </div>
    </UiForm>
    <AlertDialog :open="leaveOpen" @update:open="setLeaveOpen"><AlertDialogContent><AlertDialogHeader><AlertDialogTitle>{{ $t('challengeWriteUp.leaveTitle') }}</AlertDialogTitle><AlertDialogDescription>{{ $t('challengeWriteUp.leaveDescription') }}</AlertDialogDescription></AlertDialogHeader><AlertDialogFooter><AlertDialogCancel @click="stay">{{ $t('challengeWriteUp.stay') }}</AlertDialogCancel><AlertDialogAction @click="discard">{{ $t('challengeWriteUp.leave') }}</AlertDialogAction></AlertDialogFooter></AlertDialogContent></AlertDialog>
    <AlertDialog :open="publishOpen" @update:open="setPublishOpen"><AlertDialogContent><AlertDialogHeader><AlertDialogTitle>{{ $t('challengeWriteUp.publishTitle') }}</AlertDialogTitle><AlertDialogDescription>{{ $t('challengeWriteUp.publishDescription') }}</AlertDialogDescription></AlertDialogHeader><p>{{ root?.challengeTitle }} · {{ $t('challengeWriteUp.version', { number: root?.submitted?.number ?? 1 }) }}</p><p>{{ $t('challengeWriteUp.deduction', { percent: access?.settings?.deductionPercent ?? 20, retained: 100 - (access?.settings?.deductionPercent ?? 20) }) }}</p><AlertDialogFooter><AlertDialogCancel :disabled="pending">{{ $t('common.action.cancel') }}</AlertDialogCancel><Button :disabled="pending" @click="publish"><Spinner v-if="pending" />{{ $t('challengeWriteUp.publish') }}</Button></AlertDialogFooter></AlertDialogContent></AlertDialog>
  </section>
</template>
