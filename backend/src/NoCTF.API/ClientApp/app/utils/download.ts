import { ApiError, parseApiError } from './api-error'
import { message as describeMessage, translate } from './i18n'

export type ProtectedDownloadRequest = () => PromiseLike<Response>
export type ProtectedDownloadRequestFactory = ProtectedDownloadRequest
export type ProtectedDownloadOutcome = 'downloaded' | 'canceled'
export interface ProtectedDownload { blob: Blob; fileName: string }
type SaveFileHandle = { createWritable: () => Promise<WritableStream<Uint8Array>> }
type SaveFilePicker = (options: { suggestedName: string }) => Promise<SaveFileHandle>

export function sanitizeDownloadFileName(value: string, fallbackName = 'download'): string {
  const safe = value
    .replace(/[\u0000-\u001f\u007f/\\:*?"<>|]/g, '_')
    .replace(/[\u202a-\u202e\u2066-\u2069]/gi, '')
    .replace(/^\.+/, '')
    .trim()
    .slice(0, 180)
  return safe || fallbackName
}

function contentDispositionFileName(disposition: string, fallbackName: string): string {
  // Prefer the UTF-8 name over the server's ASCII fallback (important for Chinese Patch names).
  const match = /filename\*=(?:UTF-8''|")?([^";]+)/i.exec(disposition)
    ?? /filename=(?:")?([^";]+)/i.exec(disposition)
  if (!match?.[1]) return sanitizeDownloadFileName(fallbackName)
  const encoded = match[1].replace(/"$/, '')
  try {
    return sanitizeDownloadFileName(decodeURIComponent(encoded), fallbackName)
  }
  catch {
    return sanitizeDownloadFileName(encoded, fallbackName)
  }
}

async function checkedResponse(request: PromiseLike<Response>): Promise<Response> {
  const response = await request
  if (!response.ok) {
    let problem: Record<string, unknown> = {}
    try { problem = await response.json() } catch { /* Retain the HTTP status for empty failures. */ }
    throw parseApiError({ ...problem, responseStatusCode: response.status },
      describeMessage('common.error.downloadFailed.download', { status: response.status }))
  }
  return response
}

export async function readProtectedDownload(request: ProtectedDownloadRequest, fallbackName = 'download'): Promise<ProtectedDownload> {
  const response = await checkedResponse(request())
  return { blob: await response.blob(), fileName: contentDispositionFileName(response.headers.get('content-disposition') ?? '', fallbackName) }
}

export async function downloadSdkFile(request: PromiseLike<Response>, fallbackName = 'download'): Promise<void> {
  const { blob, fileName } = await readProtectedDownload(() => request, fallbackName)
  const anchor = document.createElement('a')
  const objectUrl = URL.createObjectURL(blob)
  anchor.href = objectUrl
  anchor.download = sanitizeDownloadFileName(fileName, fallbackName)
  anchor.hidden = true
  document.body.append(anchor)
  anchor.click()
  anchor.remove()
  setTimeout(() => URL.revokeObjectURL(objectUrl), 1_000)
}

export async function downloadSdkFileToDisk(request: ProtectedDownloadRequest, fallbackName = 'download'): Promise<ProtectedDownloadOutcome> {
  const browser = globalThis as typeof globalThis & { showSaveFilePicker?: SaveFilePicker }
  if (!browser.showSaveFilePicker) {
    await downloadSdkFile(request(), fallbackName)
    return 'downloaded'
  }
  let handle: SaveFileHandle
  try { handle = await browser.showSaveFilePicker({ suggestedName: sanitizeDownloadFileName(fallbackName) }) }
  catch (error) {
    if (error instanceof DOMException && error.name === 'AbortError') return 'canceled'
    await downloadSdkFile(request(), fallbackName)
    return 'downloaded'
  }
  const response = await checkedResponse(request())
  if (!response.body) throw new ApiError(translate('common.download.error.downloadResponseFormatInvalid'))
  await response.body.pipeTo(await handle.createWritable())
  return 'downloaded'
}
