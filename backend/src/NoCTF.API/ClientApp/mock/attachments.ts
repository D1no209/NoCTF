import type { Data } from './schema'

function safeMockFileName(value: unknown) {
  const name = String(value ?? '').replace(/[\u0000-\u001f\u007f/\\:*?"<>|]/g, '_').replace(/^\.+/, '').slice(0, 128)
  return name || 'mock-attachment.txt'
}

/** Download-only response: fixed local content, no sniffing, no active document capabilities. */
export function mockAttachmentResponse(attachment: Data) {
  const fileName = safeMockFileName(attachment.fileName)
  return new Response(String(attachment.mockContent ?? ''), { headers: {
    'Content-Type': 'text/plain; charset=utf-8',
    'Content-Length': String(attachment.byteLength),
    'Content-Disposition': `attachment; filename="${fileName}"; filename*=UTF-8''${encodeURIComponent(fileName)}`,
    'Content-Security-Policy': "sandbox; default-src 'none'",
    'X-Content-Type-Options': 'nosniff',
    'Cross-Origin-Resource-Policy': 'same-origin',
    'Cache-Control': 'no-store',
    'X-NoCTF-Mock': 'true',
  } })
}
