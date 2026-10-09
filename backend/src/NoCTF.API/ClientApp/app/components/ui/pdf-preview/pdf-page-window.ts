export interface PdfPageSize { width: number; height: number }
export interface PdfPageLayout extends PdfPageSize { number: number; top: number; scale: number }
export interface PdfScrollAnchor { page: number; ratio: number }

export const pdfPageGap = 16
export const maximumPdfCanvases = 8
const maximumCanvasPixels = 4_000_000
const maximumCanvasDimension = 8192

// Unknown pages use the first page's dimensions until they enter the reading window.
// Keeping only dimensions for visited pages avoids requesting every PDF page up front.
export function createPdfLayout(count: number, knownSizes: ReadonlyMap<number, PdfPageSize>, fallback: PdfPageSize, width: number, zoom: number): PdfPageLayout[] {
  let top = pdfPageGap
  return Array.from({ length: count }, (_, index) => {
    const number = index + 1
    const size = knownSizes.get(number) ?? fallback
    const scale = Math.min(4, Math.max(1, width - pdfPageGap * 2) / size.width) * zoom
    const page = { number, top, scale, width: size.width * scale, height: size.height * scale }
    top += page.height + pdfPageGap
    return page
  })
}

export function pdfPageAtOffset(pages: readonly PdfPageLayout[], offset: number): number {
  let low = 0
  let high = pages.length - 1
  while (low < high) {
    const middle = Math.ceil((low + high) / 2)
    if (pages[middle]!.top <= offset) low = middle
    else high = middle - 1
  }
  return low
}

export function pdfReadingWindow(pages: readonly PdfPageLayout[], top: number, height: number): PdfPageLayout[] {
  if (!pages.length) return []
  const first = pdfPageAtOffset(pages, top)
  const last = pdfPageAtOffset(pages, top + height)
  const start = Math.max(0, first - 1)
  return pages.slice(start, Math.min(pages.length, last + 2, start + maximumPdfCanvases))
}

export function capturePdfAnchor(pages: readonly PdfPageLayout[], top: number): PdfScrollAnchor {
  const page = pages[pdfPageAtOffset(pages, top + pdfPageGap)]
  return page
    ? { page: page.number, ratio: Math.max(0, Math.min(1, (top + pdfPageGap - page.top) / page.height)) }
    : { page: 1, ratio: 0 }
}

export function restorePdfAnchor(pages: readonly PdfPageLayout[], anchor: PdfScrollAnchor): number {
  const page = pages[anchor.page - 1]
  return page ? Math.max(0, page.top + page.height * anchor.ratio - pdfPageGap) : 0
}

export function pdfCanvasSize(page: PdfPageSize, deviceScale: number) {
  const scale = Math.min(deviceScale || 1, 2, Math.sqrt(maximumCanvasPixels / (page.width * page.height)),
    maximumCanvasDimension / page.width, maximumCanvasDimension / page.height)
  return { scale, width: Math.max(1, Math.floor(page.width * scale)), height: Math.max(1, Math.floor(page.height * scale)) }
}
