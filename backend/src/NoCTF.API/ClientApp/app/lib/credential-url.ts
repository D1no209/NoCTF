/**
 * 凭证(Bearer、幂等键等)只允许发往本源的 http(s) URL。
 *
 * pdf.js 的 httpHeaders 不做任何同源判断(pdf.mjs createHeaders 只看协议),
 * 因此在把 source + httpHeaders 交给第三方渲染器之前必须自行校验。
 * 仅客户端使用,默认基准取 globalThis.location.origin。
 */
export function isCredentialSafeUrl(
  value: string | null | undefined,
  origin: string | undefined = typeof globalThis.location === 'undefined' ? undefined : globalThis.location.origin,
): boolean {
  if (!value || !origin)
    return false

  try {
    const url = new URL(value, origin)
    return url.origin === origin
      && (url.protocol === 'http:' || url.protocol === 'https:')
      && !url.username && !url.password
  }
  catch {
    return false
  }
}
