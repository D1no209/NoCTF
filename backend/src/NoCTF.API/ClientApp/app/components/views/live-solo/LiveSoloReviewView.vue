<script setup lang="ts">
import { toRefs } from 'vue'
import type { LiveSoloReviewState } from '~/features/live-solo/useLiveSoloReview'
const props = defineProps<{ state: LiveSoloReviewState }>()
const { staff, loading, reading, downloading, error, options, selected, current, content, select, load, read, download, previewSource, previewHeaders, previewId, preview } = toRefs(props.state)
</script>
<template>
  <section class="flex min-w-0 flex-col gap-4">
    <div class="flex flex-wrap items-center gap-3"><h2 class="font-semibold">{{ $t(staff ? 'liveSolo.review.staffTitle' : 'liveSolo.review.title') }}</h2><Button size="sm" variant="ghost" :disabled="loading" @click="load">{{ $t('common.label.refresh') }}</Button></div>
    <p class="max-w-[75ch] text-sm text-muted-foreground">{{ $t(staff ? 'liveSolo.review.staffHelp' : 'liveSolo.review.help') }}</p>
    <Alert v-if="error" variant="destructive"><AlertDescription>{{ $message(error) }}</AlertDescription></Alert>
    <Skeleton v-if="loading" class="h-32" />
    <Empty v-else-if="!options.length"><EmptyHeader><EmptyTitle>{{ $t('liveSolo.review.empty') }}</EmptyTitle></EmptyHeader></Empty>
    <template v-else>
      <ChoicePicker :items="options" :model-value="selected" :label="$t(staff ? 'liveSolo.review.questionLabel' : 'liveSolo.questions')" :search-label="$t('liveSolo.questionSearch')" :empty-label="$t('liveSolo.review.empty')" @update:model-value="select" />
      <div v-if="current" class="flex flex-wrap gap-2"><Badge variant="secondary">{{ $t('liveSolo.round', { number: current.roundNumber ?? 1 }) }}</Badge><Badge variant="secondary">{{ $t(current.openedAt ? 'liveSolo.review.opened' : 'liveSolo.review.future') }}</Badge></div>
      <Skeleton v-if="reading" class="h-40" />
      <p v-else-if="current?.contentRecorded === false" class="text-sm text-muted-foreground">{{ $t('liveSolo.review.unrecorded') }}</p>
      <template v-else-if="content"><h3 class="text-lg font-semibold">{{ content.question?.title }}</h3><div class="flex flex-wrap gap-2"><Badge v-for="tag in content.question?.tags" :key="tag" variant="secondary">{{ tag }}</Badge></div><MarkdownContent :source="content.question?.description ?? ''" class="max-w-[75ch]" />
        <section v-if="content.hints?.length" class="flex flex-col gap-3"><h4 class="font-semibold">{{ $t('liveSolo.hints.title') }}</h4><MarkdownQuote v-for="hint in content.hints" :key="hint.id" :source="hint.content ?? ''" /></section>
        <div class="flex min-w-0 flex-col gap-3"><div v-for="file in content.attachments" :key="file.id" class="flex flex-wrap items-center gap-2"><Button variant="outline" :disabled="downloading" class="h-auto min-h-10 max-w-full whitespace-normal break-all text-left" @click="download(file.id!)">{{ file.fileName }}</Button><Button v-if="file.contentType === 'application/pdf'" size="sm" variant="ghost" :disabled="downloading" @click="preview(file.id!)">{{ $t('liveSolo.review.preview') }}</Button></div></div>
        <PdfPreview v-if="previewSource" :key="previewId ?? undefined" :source="previewSource" :http-headers="previewHeaders" :accessible-label="$t('liveSolo.review.pdf')" :empty-label="$t('liveSolo.review.failed')" />
      </template>
      <Button v-if="error" variant="outline" @click="read">{{ $t('common.label.retry') }}</Button>
    </template>
  </section>
</template>
