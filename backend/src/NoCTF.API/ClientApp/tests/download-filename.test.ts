import { expect, test } from 'bun:test'
import { sanitizeDownloadFileName } from '../app/utils/download'

test('download names cannot contain paths, controls or bidi overrides', () => {
  expect(sanitizeDownloadFileName('../../秘密\u202egpj.exe')).toBe('_.._秘密gpj.exe')
  expect(sanitizeDownloadFileName('folder\\payload.html')).toBe('folder_payload.html')
  expect(sanitizeDownloadFileName('\r\nheader.txt')).toBe('__header.txt')
  expect(sanitizeDownloadFileName('...')).toBe('download')
})
