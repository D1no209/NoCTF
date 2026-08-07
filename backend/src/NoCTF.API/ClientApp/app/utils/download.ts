import { getAccessToken } from '~/lib/session'

/**
 * 下载需要 Bearer 鉴权的文件(挑战附件等),通过 blob + 临时链接触发浏览器下载。
 * 文件名优先取 Content-Disposition,其次用传入的 fallback。
 */
export async function downloadProtectedFile(url: string, fallbackName = 'download'): Promise<void> {
  const token = getAccessToken()
  const response = await fetch(url, {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  })
  if (!response.ok) {
    throw new ApiError(`下载失败(HTTP ${response.status})`, { status: response.status })
  }
  const disposition = response.headers.get('content-disposition') ?? ''
  const match = /filename\*?=(?:UTF-8''|")?([^";]+)/i.exec(disposition)
  const name = match?.[1] ? decodeURIComponent(match[1].replace(/"$/, '')) : fallbackName
  const blob = await response.blob()
  const anchor = document.createElement('a')
  const objectUrl = URL.createObjectURL(blob)
  anchor.href = objectUrl
  anchor.download = name
  anchor.click()
  URL.revokeObjectURL(objectUrl)
}
