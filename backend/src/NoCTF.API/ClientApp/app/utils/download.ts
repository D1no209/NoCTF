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
  const match = /filename\*?=(?:UTF-8''|")?([^";]+)/i.exec(disposition)
  if (!match?.[1]) return fallbackName
  const encoded = match[1].replace(/"$/, '')
  try {
    return decodeURIComponent(encoded)
  }
  catch {
    return encoded
  }
}

export async function readProtectedDownload(
  request: ProtectedDownloadRequest,
  fallbackName = 'download',
): Promise<ProtectedDownload> {
  const { data, error, response } = await request()
  if (error || !response?.ok) {
    throw parseApiError(
      error,
      translate('下载失败（HTTP {status}）', { status: response?.status ?? 0 }),
    )
  }
  if (!(data instanceof Blob)) {
    throw new ApiError(translate('下载响应格式无效'))
  }
  return {
    blob: data,
    fileName: contentDispositionFileName(
      response.headers.get('content-disposition') ?? '',
      fallbackName,
    ),
  }
}

/**
 * 下载需要 Bearer 鉴权的文件(挑战附件等),通过 blob + 临时链接触发浏览器下载。
 * 文件名优先取 Content-Disposition,其次用传入的 fallback。
 */
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
