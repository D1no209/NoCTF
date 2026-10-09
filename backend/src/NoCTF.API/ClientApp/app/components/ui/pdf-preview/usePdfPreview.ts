import type { ComponentPublicInstance } from 'vue'
import type { PDFDocumentLoadingTask, PDFDocumentProxy, RenderTask } from 'pdfjs-dist'
import { computed, nextTick, onBeforeUnmount, ref, shallowRef, watch } from 'vue'
import pdfWorkerUrl from 'pdfjs-dist/build/pdf.worker.min.mjs?url'
import { capturePdfAnchor, createPdfLayout, pdfCanvasSize, pdfPageAtOffset, pdfPageGap, pdfReadingWindow, restorePdfAnchor } from './pdf-page-window'
import type { PdfPageLayout, PdfPageSize } from './pdf-page-window'

interface PdfPreviewOptions { source?: string | null; loading?: boolean }
interface PageRender {
  canvas: HTMLCanvasElement
  task?: RenderTask
  done: boolean
  failed: boolean
  text: string
}

export function usePdfPreview(props: PdfPreviewOptions) {
  const minimumZoom = 0.5
  const maximumZoom = 3
  const viewportHost = ref<HTMLElement | null>(null)
  const pdfDocument = shallowRef<PDFDocumentProxy | null>(null)
  const pageNumber = ref(1)
  const pageCount = ref(0)
  const zoomFactor = ref(1)
  const documentLoading = ref(false)
  const renderPending = ref(false)
  const internalError = ref(false)
  const width = ref(1)
  const scrollTop = ref(0)
  const viewportHeight = ref(1)
  const fallbackSize = shallowRef<PdfPageSize>({ width: 612, height: 792 })
  const knownSizes = shallowRef(new Map<number, PdfPageSize>())
  const pageStates = shallowRef(new Map<number, { done: boolean; failed: boolean; text: string }>())
  const renders = new Map<number, PageRender>()
  let scrollTarget: HTMLElement | null = null
  let documentRequest = 0
  let loadingTask: PDFDocumentLoadingTask | null = null
  let resizeObserver: ResizeObserver | null = null
  let updateFrame = 0
  let rendering = false
  let pdfLibraryPromise: Promise<typeof import('pdfjs-dist')> | null = null

  const pages = computed(() => createPdfLayout(pageCount.value, knownSizes.value, fallbackSize.value, width.value, zoomFactor.value))
  const visiblePages = computed(() => pdfReadingWindow(pages.value, scrollTop.value, viewportHeight.value))
  const stageHeight = computed(() => {
    const last = pages.value.at(-1)
    return last ? last.top + last.height + pdfPageGap : 0
  })
  const stageWidth = computed(() => Math.max(width.value, ...visiblePages.value.map(page => page.width + pdfPageGap * 2)))
  const initialLoading = computed(() => (props.loading || documentLoading.value) && pdfDocument.value === null)
  const busy = computed(() => Boolean(props.loading || documentLoading.value || renderPending.value))
  const canGoBack = computed(() => pageNumber.value > 1)
  const canGoForward = computed(() => pageNumber.value < pageCount.value)
  const canZoomOut = computed(() => zoomFactor.value > minimumZoom)
  const canZoomIn = computed(() => zoomFactor.value < maximumZoom)
  const zoomPercent = computed(() => Math.round(zoomFactor.value * 100))

  function publishPageStates() {
    pageStates.value = new Map([...renders].map(([number, { done, failed, text }]) => [number, { done, failed, text }]))
    renderPending.value = [...renders.values()].some(state => !state.done && !state.failed)
  }

  function releaseRender(state: PageRender) {
    state.task?.cancel()
    // Detached canvases keep their backing store until explicitly reset.
    state.canvas.width = state.canvas.height = 0
  }

  function clearRenders() {
    for (const state of renders.values()) releaseRender(state)
    renders.clear()
    publishPageStates()
  }

  async function disposeDocument() {
    clearRenders()
    resizeObserver?.disconnect()
    resizeObserver = null
    if (updateFrame) cancelAnimationFrame(updateFrame)
    updateFrame = 0
    scrollTarget = null
    const task = loadingTask
    loadingTask = null
    pdfDocument.value = null
    pageCount.value = 0
    knownSizes.value = new Map()
    if (task) await task.destroy().catch(() => undefined)
  }

  function updateReadingWindow() {
    if (!scrollTarget) return
    scrollTop.value = scrollTarget.scrollTop
    viewportHeight.value = scrollTarget.clientHeight
    pageNumber.value = (pages.value[pdfPageAtOffset(pages.value, scrollTop.value + Math.min(120, viewportHeight.value / 3))]?.number ?? 1)
    const wanted = new Set(visiblePages.value.map(page => page.number))
    for (const [number, state] of renders) {
      if (wanted.has(number)) continue
      releaseRender(state)
      renders.delete(number)
    }
    publishPageStates()
    void renderWindow()
  }

  function onScroll() {
    if (updateFrame) return
    updateFrame = requestAnimationFrame(() => {
      updateFrame = 0
      updateReadingWindow()
    })
  }

  async function preservePosition(change: () => void) {
    const anchor = capturePdfAnchor(pages.value, scrollTarget?.scrollTop ?? 0)
    change()
    const top = restorePdfAnchor(pages.value, anchor)
    scrollTop.value = top
    await nextTick()
    if (scrollTarget) scrollTarget.scrollTop = top
    updateReadingWindow()
  }

  async function rememberSize(number: number, size: PdfPageSize) {
    const previous = knownSizes.value.get(number)
    if (previous?.width === size.width && previous.height === size.height) return
    await preservePosition(() => {
      knownSizes.value = new Map(knownSizes.value).set(number, size)
    })
  }

  function setCanvas(number: number, element: Element | ComponentPublicInstance | null) {
    if (!(element instanceof HTMLCanvasElement)) return
    if (renders.get(number)?.canvas === element) return
    renders.set(number, { canvas: element, done: false, failed: false, text: '' })
    publishPageStates()
    void renderWindow()
  }

  function setViewportHost(element: Element | ComponentPublicInstance | null) {
    viewportHost.value = element instanceof HTMLElement ? element : null
  }

  async function renderPage(document: PDFDocumentProxy, number: number, state: PageRender, request: number) {
    const page = await document.getPage(number)
    const current = () => request === documentRequest && pdfDocument.value === document && renders.get(number) === state
    try {
      if (!current()) return
      const base = page.getViewport({ scale: 1 })
      await rememberSize(number, { width: base.width, height: base.height })
      if (!current()) return
      const layout = pages.value[number - 1]!
      const viewport = page.getViewport({ scale: layout.scale })
      const output = pdfCanvasSize(viewport, window.devicePixelRatio)
      const canvas = state.canvas
      const context = canvas.getContext('2d', { alpha: false })
      if (!context) throw new Error('Canvas 2D context is unavailable')
      canvas.width = output.width
      canvas.height = output.height
      const transform: [number, number, number, number, number, number] = [output.scale, 0, 0, output.scale, 0, 0]
      state.task = page.render({ canvas, canvasContext: context, viewport, transform })
      const [, textContent] = await Promise.all([state.task.promise, page.getTextContent()])
      if (!current()) return
      state.text = textContent.items.map(item => 'str' in item ? item.str : '').filter(Boolean).join(' ')
      state.done = true
    }
    finally {
      // Only release PDF.js page resources after rendering (including cancellation) settles.
      page.cleanup()
      state.task = undefined
    }
  }

  async function renderWindow() {
    if (rendering || !pdfDocument.value) return
    rendering = true
    try {
      while (pdfDocument.value) {
        const document = pdfDocument.value
        const request = documentRequest
        // Visible pages first, then one neighbour on either side. Render one at a time.
        const candidates = [...visiblePages.value].sort((a, b) => Math.abs(a.number - pageNumber.value) - Math.abs(b.number - pageNumber.value))
        const next = candidates.find(page => {
          const state = renders.get(page.number)
          return state && !state.done && !state.failed
        })
        if (!next) break
        const state = renders.get(next.number)!
        try { await renderPage(document, next.number, state, request) }
        catch (error) {
          if (renders.get(next.number) === state && !(error instanceof Error && error.name === 'RenderingCancelledException')) state.failed = true
        }
        publishPageStates()
      }
    }
    finally { rendering = false }
  }

  function retryPage(number: number) {
    const state = renders.get(number)
    if (!state) return
    state.failed = false
    publishPageStates()
    void renderWindow()
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
    scrollTop.value = 0
    zoomFactor.value = 1
    try {
      pdfLibraryPromise ??= import('pdfjs-dist')
      const library = await pdfLibraryPromise
      library.GlobalWorkerOptions.workerSrc = pdfWorkerUrl
      if (request !== documentRequest) return
      const task = library.getDocument({ url: source, enableXfa: false, disableAutoFetch: true, disableStream: true })
      loadingTask = task
      const document = await task.promise
      if (request !== documentRequest) { await task.destroy().catch(() => undefined); return }
      const first = await document.getPage(1)
      const size = first.getViewport({ scale: 1 })
      first.cleanup()
      if (request !== documentRequest) return
      fallbackSize.value = { width: size.width, height: size.height }
      knownSizes.value = new Map([[1, fallbackSize.value]])
      pdfDocument.value = document
      pageCount.value = document.numPages
      await nextTick()
      if (request !== documentRequest) return
      scrollTarget = viewportHost.value?.querySelector<HTMLElement>('[data-slot="scroll-surface"]') ?? null
      if (!scrollTarget) return
      scrollTarget.scrollTop = scrollTarget.scrollLeft = 0
      width.value = scrollTarget.clientWidth
      resizeObserver = new ResizeObserver(() => {
        if (!scrollTarget) return
        const nextWidth = scrollTarget.clientWidth
        if (nextWidth !== width.value) {
          void preservePosition(() => { width.value = nextWidth; clearRenders() })
        }
        else updateReadingWindow()
      })
      resizeObserver.observe(scrollTarget)
      updateReadingWindow()
    }
    catch {
      if (request === documentRequest) {
        internalError.value = true
        await disposeDocument()
      }
    }
    finally { if (request === documentRequest) documentLoading.value = false }
  }

  async function goToPage(number: number) {
    const page = pages.value[number - 1]
    if (!page || !scrollTarget) return
    const top = Math.max(0, page.top - pdfPageGap)
    scrollTop.value = top
    pageNumber.value = number
    await nextTick()
    if (scrollTarget) scrollTarget.scrollTop = top
    updateReadingWindow()
  }

  function setZoom(zoom: number) {
    void preservePosition(() => { zoomFactor.value = zoom; clearRenders() })
  }
  function previousPage() { if (canGoBack.value) void goToPage(pageNumber.value - 1) }
  function nextPage() { if (canGoForward.value) void goToPage(pageNumber.value + 1) }
  function zoomOut() { if (canZoomOut.value) setZoom(Math.max(minimumZoom, zoomFactor.value - 0.25)) }
  function zoomIn() { if (canZoomIn.value) setZoom(Math.min(maximumZoom, zoomFactor.value + 0.25)) }
  function fitWidth() { if (zoomFactor.value !== 1) setZoom(1) }
  function pageStyle(page: PdfPageLayout) { return { top: `${page.top}px`, width: `${page.width}px`, height: `${page.height}px` } }

  watch(() => props.source, source => {
    if (import.meta.client) void loadDocument(source ?? null)
  }, { immediate: true })
  onBeforeUnmount(() => { documentRequest++; void disposeDocument() })

  return { viewportHost, pdfDocument, pageNumber, pageCount, zoomFactor, initialLoading, busy, renderPending,
    internalError, canGoBack, canGoForward, canZoomOut, canZoomIn, zoomPercent, visiblePages, stageHeight, stageWidth, pageStates,
    setViewportHost, setCanvas, onScroll, previousPage, nextPage, zoomOut, zoomIn, fitWidth, retryPage, pageStyle }
}
