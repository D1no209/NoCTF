<script setup lang="ts">
import type { PDFDocumentLoadingTask, PDFDocumentProxy, PDFPageProxy, RenderTask } from 'pdfjs-dist'
import { ChevronLeft, ChevronRight, FileWarning, Maximize2, Minus, Plus } from '@lucide/vue'
import { computed, nextTick, onBeforeUnmount, ref, shallowRef, watch } from 'vue'
import pdfWorkerUrl from 'pdfjs-dist/build/pdf.worker.min.mjs?url'

const props = defineProps<{
  source?: string | null
  accessibleLabel: string
  emptyLabel: string
  loading?: boolean
  error?: string | null
  fill?: boolean
}>()

const minimumZoom = 0.5
const maximumZoom = 3
const canvas = ref<HTMLCanvasElement | null>(null)
const viewportHost = ref<HTMLElement | null>(null)
const pdfDocument = shallowRef<PDFDocumentProxy | null>(null)
const pageNumber = ref(1)
const pageCount = ref(0)
const zoomFactor = ref(1)
const documentLoading = ref(false)
const renderPending = ref(false)
const internalError = ref(false)
const accessiblePageText = ref('')

let documentRequest = 0
let renderRequest = 0
let loadingTask: PDFDocumentLoadingTask | null = null
let renderedPage: PDFPageProxy | null = null
let renderTask: RenderTask | null = null
let resizeObserver: ResizeObserver | null = null
let resizeFrame = 0
let observedWidth = 0
let pdfLibraryPromise: Promise<typeof import('pdfjs-dist')> | null = null

const initialLoading = computed(() =>
  (props.loading || documentLoading.value) && pdfDocument.value === null,
)
const busy = computed(() => Boolean(props.loading || documentLoading.value || renderPending.value))
const canGoBack = computed(() => pageNumber.value > 1 && !renderPending.value)
const canGoForward = computed(() => pageNumber.value < pageCount.value && !renderPending.value)
const canZoomOut = computed(() => zoomFactor.value > minimumZoom && !renderPending.value)
const canZoomIn = computed(() => zoomFactor.value < maximumZoom && !renderPending.value)
const zoomPercent = computed(() => Math.round(zoomFactor.value * 100))

async function pdfLibrary() {
  pdfLibraryPromise ??= import('pdfjs-dist')
  const library = await pdfLibraryPromise
  library.GlobalWorkerOptions.workerSrc = pdfWorkerUrl
  return library
}

async function disposeDocument() {
  renderRequest++
  renderTask?.cancel()
  renderTask = null
  renderPending.value = false
  renderedPage?.cleanup()
  renderedPage = null
  resizeObserver?.disconnect()
  resizeObserver = null
  if (resizeFrame) cancelAnimationFrame(resizeFrame)
  resizeFrame = 0
  observedWidth = 0
  const task = loadingTask
  loadingTask = null
  pdfDocument.value = null
  pageCount.value = 0
  accessiblePageText.value = ''
  if (task) await task.destroy().catch(() => undefined)
}

function connectResizeObserver() {
  resizeObserver?.disconnect()
  const host = viewportHost.value
  if (!host) return
  observedWidth = Math.round(host.clientWidth)
  resizeObserver = new ResizeObserver(entries => {
    const width = Math.round(entries[0]?.contentRect.width ?? 0)
    if (!width || width === observedWidth) return
    observedWidth = width
    if (resizeFrame) cancelAnimationFrame(resizeFrame)
    resizeFrame = requestAnimationFrame(() => {
      resizeFrame = 0
      void renderPage()
    })
  })
  resizeObserver.observe(host)
}

async function renderPage() {
  const document = pdfDocument.value
  const target = canvas.value
  const host = viewportHost.value
  if (!document || !target || !host) return
  const request = ++renderRequest
  renderTask?.cancel()
  renderTask = null
  renderedPage?.cleanup()
  renderedPage = null
  renderPending.value = true
  internalError.value = false
  accessiblePageText.value = ''
  try {
    const page = await document.getPage(pageNumber.value)
    if (request !== renderRequest) {
      page.cleanup()
      return
    }
    renderedPage = page
    const baseViewport = page.getViewport({ scale: 1 })
    const availableWidth = Math.max(1, host.clientWidth - 32)
    const fitScale = Math.min(4, availableWidth / baseViewport.width)
    const viewport = page.getViewport({ scale: fitScale * zoomFactor.value })
    const outputScale = Math.min(window.devicePixelRatio || 1, 2)
    const context = target.getContext('2d', { alpha: false })
    if (!context) throw new Error('Canvas 2D context is unavailable')
    target.width = Math.floor(viewport.width * outputScale)
    target.height = Math.floor(viewport.height * outputScale)
    target.style.width = `${Math.floor(viewport.width)}px`
    target.style.height = `${Math.floor(viewport.height)}px`
    const transform: [number, number, number, number, number, number] | undefined
      = outputScale === 1 ? undefined : [outputScale, 0, 0, outputScale, 0, 0]
    const currentRender = page.render({
      canvas: target,
      canvasContext: context,
      viewport,
      transform,
    })
    renderTask = currentRender
    const [, textContent] = await Promise.all([currentRender.promise, page.getTextContent()])
    if (request !== renderRequest) return
    accessiblePageText.value = textContent.items
      .map(item => 'str' in item ? item.str : '')
      .filter(Boolean)
      .join(' ')
  }
  catch (error) {
    if (request === renderRequest && !(error instanceof Error && error.name === 'RenderingCancelledException'))
      internalError.value = true
  }
  finally {
    if (request === renderRequest) renderPending.value = false
  }
}

async function loadDocument(source: string | null) {
  const request = ++documentRequest
  documentLoading.value = false
  await disposeDocument()
  if (request !== documentRequest) return
  internalError.value = false
  if (!source) return
  documentLoading.value = true
  pageNumber.value = 1
  zoomFactor.value = 1
  try {
    const library = await pdfLibrary()
    if (request !== documentRequest) return
    const task = library.getDocument({
      url: source,
      enableXfa: false,
    })
    loadingTask = task
    const document = await task.promise
    if (request !== documentRequest) {
      await task.destroy().catch(() => undefined)
      return
    }
    pdfDocument.value = document
    pageCount.value = document.numPages
    await nextTick()
    connectResizeObserver()
    await renderPage()
  }
  catch {
    if (request === documentRequest) {
      internalError.value = true
      const task = loadingTask
      loadingTask = null
      if (task) await task.destroy().catch(() => undefined)
    }
  }
  finally {
    if (request === documentRequest) documentLoading.value = false
  }
}

function previousPage() {
  if (!canGoBack.value) return
  pageNumber.value--
  void renderPage()
}

function nextPage() {
  if (!canGoForward.value) return
  pageNumber.value++
  void renderPage()
}

function zoomOut() {
  if (!canZoomOut.value) return
  zoomFactor.value = Math.max(minimumZoom, zoomFactor.value - 0.25)
  void renderPage()
}

function zoomIn() {
  if (!canZoomIn.value) return
  zoomFactor.value = Math.min(maximumZoom, zoomFactor.value + 0.25)
  void renderPage()
}

function fitWidth() {
  if (renderPending.value || zoomFactor.value === 1) return
  zoomFactor.value = 1
  void renderPage()
}

watch(() => props.source, source => {
  if (import.meta.client) void loadDocument(source ?? null)
}, { immediate: true })

onBeforeUnmount(() => {
  documentRequest++
  void disposeDocument()
})
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
    </Empty>

    <template v-else-if="pdfDocument">
      <div data-slot="pdf-preview-toolbar">
        <div role="group" :aria-label="$t('pdfPreview.pageNavigation')">
          <Button variant="ghost" size="icon-sm" :disabled="!canGoBack" :aria-label="$t('ui.previousPage')" @click="previousPage">
            <ChevronLeft />
          </Button>
          <span class="min-w-24 text-center font-mono text-xs tabular-nums" aria-live="polite">
            {{ $t('pdfPreview.pageStatus', { page: pageNumber, total: pageCount }) }}
          </span>
          <Button variant="ghost" size="icon-sm" :disabled="!canGoForward" :aria-label="$t('ui.nextPage')" @click="nextPage">
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
          <Button variant="ghost" size="icon-sm" :disabled="renderPending || zoomFactor === 1" :aria-label="$t('pdfPreview.fitWidth')" @click="fitWidth">
            <Maximize2 />
          </Button>
          <span v-if="renderPending" class="flex items-center gap-1.5 text-xs text-muted-foreground" role="status">
            <Spinner class="size-3" />{{ $t('pdfPreview.rendering') }}
          </span>
        </div>
      </div>

      <div ref="viewportHost" class="min-h-0 flex-1">
        <ScrollSurface axis="both" class="h-full w-full" :aria-label="accessibleLabel">
          <div data-slot="pdf-page-stage">
            <div data-slot="pdf-page" role="document" :aria-label="$t('pdfPreview.pageStatus', { page: pageNumber, total: pageCount })">
              <canvas ref="canvas" aria-hidden="true" />
            </div>
            <p class="sr-only">
              {{ $t('pdfPreview.accessiblePage', { page: pageNumber, total: pageCount, text: accessiblePageText }) }}
            </p>
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
