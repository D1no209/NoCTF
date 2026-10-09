<script setup lang="ts">
import type { UiMessage } from '../../../utils/i18n'
import { ChevronLeft, ChevronRight, FileWarning, Maximize2, Minus, Plus } from '@lucide/vue'
import { usePdfPreview } from './usePdfPreview'

const props = defineProps<{
  source?: string | null
  accessibleLabel: string
  emptyLabel: string
  loading?: boolean
  error?: UiMessage | null
  fill?: boolean
  retryable?: boolean
}>()
const emit = defineEmits<{ retry: [] }>()
const { pdfDocument, pageNumber, pageCount, zoomFactor, initialLoading, busy, renderPending,
  internalError, canGoBack, canGoForward, canZoomOut, canZoomIn, zoomPercent, visiblePages, stageHeight, stageWidth, pageStates,
  setViewportHost, setCanvas, onScroll, previousPage, nextPage, zoomOut, zoomIn, fitWidth, retryPage, pageStyle } = usePdfPreview(props)
</script>

<template>
  <div
    data-slot="pdf-preview"
    role="region"
    :aria-label="accessibleLabel"
    :aria-busy="busy || undefined"
    class="flex h-full min-w-0 flex-col overflow-hidden rounded-xl bg-muted/45 shadow-inner"
    :class="fill ? 'min-h-0' : 'min-h-[32rem]'"
  >
    <Skeleton v-if="initialLoading" class="h-full w-full" :class="fill ? 'min-h-0' : 'min-h-[32rem]'" />

    <Empty v-else-if="error || internalError" class="h-full" :class="fill ? 'min-h-0' : 'min-h-[32rem]'">
      <EmptyHeader>
        <EmptyMedia variant="icon"><FileWarning /></EmptyMedia>
        <EmptyTitle>{{ error ? $message(error) : $t('pdfPreview.loadFailed') }}</EmptyTitle>
        <EmptyDescription>{{ $t('pdfPreview.loadFailedDescription') }}</EmptyDescription>
      </EmptyHeader>
      <EmptyContent v-if="retryable"><Button type="button" variant="outline" :disabled="busy" @click="emit('retry')">{{ $t('common.label.retry') }}</Button></EmptyContent>
    </Empty>

    <template v-else-if="pdfDocument">
      <div data-slot="pdf-preview-toolbar">
        <div role="group" :aria-label="$t('pdfPreview.pageNavigation')">
          <Button variant="ghost" size="icon-sm" :disabled="!canGoBack" :aria-label="$t('common.label.previousPage')" @click="previousPage">
            <ChevronLeft />
          </Button>
          <span class="min-w-24 text-center font-mono text-xs tabular-nums" aria-live="polite">
            {{ $t('pdfPreview.pageStatus', { page: pageNumber, total: pageCount }) }}
          </span>
          <Button variant="ghost" size="icon-sm" :disabled="!canGoForward" :aria-label="$t('common.label.nextPage')" @click="nextPage">
            <ChevronRight />
          </Button>
        </div>

        <div role="group" :aria-label="$t('pdfPreview.zoomControls')">
          <Button variant="ghost" size="icon-sm" :disabled="!canZoomOut" :aria-label="$t('pdfPreview.zoomOut')" @click="zoomOut">
            <Minus />
          </Button>
          <span class="min-w-12 text-center font-mono text-xs tabular-nums">{{ zoomPercent }}%</span>
          <Button variant="ghost" size="icon-sm" :disabled="!canZoomIn" :aria-label="$t('pdfPreview.zoomIn')" @click="zoomIn">
            <Plus />
          </Button>
          <Button variant="ghost" size="icon-sm" :disabled="zoomFactor === 1" :aria-label="$t('pdfPreview.fitWidth')" @click="fitWidth">
            <Maximize2 />
          </Button>
          <span v-if="renderPending" class="flex items-center gap-1.5 text-xs text-muted-foreground" role="status">
            <Spinner class="size-3" />{{ $t('pdfPreview.rendering') }}
          </span>
        </div>
      </div>

      <div :ref="setViewportHost" class="min-h-0 flex-1">
        <ScrollSurface axis="both" class="h-full w-full" :aria-label="accessibleLabel" @scroll.passive="onScroll">
          <div data-slot="pdf-page-stage" :style="{ height: `${stageHeight}px`, width: `${stageWidth}px` }">
            <div v-for="page in visiblePages" :key="page.number" data-slot="pdf-page"
              :data-page-number="page.number" role="document" :style="pageStyle(page)"
              :aria-label="$t('pdfPreview.pageStatus', { page: page.number, total: pageCount })"
              :aria-busy="!pageStates.get(page.number)?.done || undefined">
              <canvas :ref="element => setCanvas(page.number, element)" aria-hidden="true" />
              <div v-if="pageStates.get(page.number)?.failed" data-slot="pdf-page-status">
                <FileWarning class="size-5" />
                <span>{{ $t('pdfPreview.loadFailed') }}</span>
                <Button variant="outline" size="sm" @click="retryPage(page.number)">{{ $t('common.label.retry') }}</Button>
              </div>
              <div v-else-if="!pageStates.get(page.number)?.done" data-slot="pdf-page-status" role="status">
                <Spinner class="size-4" />{{ $t('pdfPreview.rendering') }}
              </div>
              <p class="sr-only">
                {{ $t('pdfPreview.accessiblePage', { page: page.number, total: pageCount, text: pageStates.get(page.number)?.text ?? '' }) }}
              </p>
            </div>
          </div>
        </ScrollSurface>
      </div>
    </template>

    <Empty v-else class="h-full" :class="fill ? 'min-h-0' : 'min-h-[32rem]'">
      <EmptyHeader>
        <EmptyTitle>{{ emptyLabel }}</EmptyTitle>
      </EmptyHeader>
    </Empty>
  </div>
</template>

<style src="./pdf-preview.css"></style>
