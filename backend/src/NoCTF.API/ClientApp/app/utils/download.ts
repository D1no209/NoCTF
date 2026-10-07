import { message as describeMessage } from './i18n'
import { ApiError, parseApiError } from './api-error'
import { translate } from './i18n'

export interface ProtectedDownloadResponse {
  data?: unknown
  error?: unknown
  response?: Response
}

export type ProtectedDownloadRequest = () => PromiseLike<ProtectedDownloadResponse>

export interface ProtectedDownload {
  blob: Blob
  fileName: string
}

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

async function readSdkDownload(
  request: PromiseLike<ProtectedDownloadResponse>,
  fallbackName: string,
): Promise<ProtectedDownload> {
  const { data, error, response } = await request
  if (error || response?.ok === false) {
    throw parseApiError(
      error,
      describeMessage("common.error.downloadFailed.download", { status: response?.status ?? '-' }),
    )
  }
  if (!(data instanceof Blob)) {
    throw new ApiError(translate("common.download.error.downloadResponseFormatInvalid"))
  }
  return {
    blob: data,
    fileName: contentDispositionFileName(
      response?.headers.get('content-disposition') ?? '',
      fallbackName,
    ),
  }
}

export async function readProtectedDownload(
  request: ProtectedDownloadRequest,
  fallbackName = 'download',
): Promise<ProtectedDownload> {
  return readSdkDownload(request(), fallbackName)
}

/** Trigger a browser download from a generated SDK file operation. */
export async function downloadSdkFile(
  request: PromiseLike<ProtectedDownloadResponse>,
  fallbackName = 'download',
): Promise<void> {
  const { blob, fileName } = await readSdkDownload(request, fallbackName)
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

/** @deprecated Prefer downloadSdkFile with the generated SDK promise directly. */
export async function downloadProtectedFile(
  request: ProtectedDownloadRequest,
  fallbackName = 'download',
): Promise<void> {
  const { blob, fileName } = await readProtectedDownload(request, fallbackName)
  const anchor = document.createElement('a')
  const objectUrl = URL.createObjectURL(blob)
  anchor.href = objectUrl
  anchor.download = fileName
  anchor.click()
  URL.revokeObjectURL(objectUrl)
}

/** Hand the authenticated attachment stream to the browser without materializing file bytes in JavaScript. */
export function startAttachmentBrowserDownload(downloadUrl: string): void {
  const url = new URL(downloadUrl, window.location.origin)
  if (url.origin !== window.location.origin || !['https:', 'http:'].includes(url.protocol)
    || url.search || url.hash || url.username || url.password)
    throw new ApiError(translate('common.download.error.downloadResponseFormatInvalid'))
  const anchor = document.createElement('a')
  anchor.href = url.href
  anchor.download = ''
  anchor.hidden = true
  document.body.append(anchor)
  anchor.click()
  anchor.remove()
}
