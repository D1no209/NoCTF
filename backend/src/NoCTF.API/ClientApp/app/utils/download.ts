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

function contentDispositionFileName(disposition: string, fallbackName: string): string {
  // Prefer the UTF-8 name over the server's ASCII fallback (important for Chinese Patch names).
  const match = /filename\*=(?:UTF-8''|")?([^";]+)/i.exec(disposition)
    ?? /filename=(?:")?([^";]+)/i.exec(disposition)
  if (!match?.[1]) return fallbackName
  const encoded = match[1].replace(/"$/, '')
  try {
    return decodeURIComponent(encoded)
  }
  catch {
    return encoded
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
      translate("ui.downloadFailedHttp", { status: response?.status ?? '-' }),
    )
  }
  if (!(data instanceof Blob)) {
    throw new ApiError(translate("ui.theDownloadResponseFormatIsInvalid"))
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
  anchor.download = fileName
  anchor.click()
  URL.revokeObjectURL(objectUrl)
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
