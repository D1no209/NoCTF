// Zero-dependency ZIP reader for theme package archives.
//
// The admin theme-packs page imports token packs from zip files: every `.json`
// entry in the archive is a candidate `ThemePackageExport` payload. Parsing
// lives in the platform layer (`themes/`) so theme packages only wire a file
// input to `useThemePackages.importThemeArchive`.
//
// Scope is deliberately narrow and defensive:
// - Central directory driven (local-header sizes are never trusted).
// - Compression methods 0 (store) and 8 (deflate, via DecompressionStream).
// - Single-disk archives only; Zip64 is rejected explicitly.
// - Hard caps on entry count and uncompressed sizes (zip-bomb guard).

const EOCD_SIGNATURE = 0x06054b50
const CENTRAL_DIRECTORY_SIGNATURE = 0x02014b50
const LOCAL_HEADER_SIGNATURE = 0x04034b50

const EOCD_MIN_LENGTH = 22
const EOCD_MAX_SEARCH = EOCD_MIN_LENGTH + 0xffff
const CENTRAL_DIRECTORY_FIXED_LENGTH = 46
const LOCAL_HEADER_FIXED_LENGTH = 30

const METHOD_STORE = 0
const METHOD_DEFLATE = 8
const ZIP64_SENTINEL = 0xffffffff

export const THEME_ARCHIVE_MAX_ENTRIES = 32
export const THEME_ARCHIVE_MAX_ENTRY_BYTES = 1024 * 1024
export const THEME_ARCHIVE_MAX_TOTAL_BYTES = 4 * 1024 * 1024

export interface ThemeArchiveEntry {
  /** Entry path inside the archive, e.g. `themes/pixel.theme.json`. */
  name: string
  /** Decoded UTF-8 file contents. */
  text: string
}

interface CentralDirectoryEntry {
  name: string
  method: number
  compressedSize: number
  uncompressedSize: number
  localHeaderOffset: number
}

function isThemeEntryName(name: string) {
  const normalized = name.replaceAll('\\', '/').toLowerCase()
  if (normalized.endsWith('/'))
    return false
  if (normalized.startsWith('__macosx/') || normalized.includes('/__macosx/'))
    return false
  if (normalized.split('/').some(part => part === '..'))
    return false
  return normalized.endsWith('.json')
}

function readCentralDirectory(view: DataView): CentralDirectoryEntry[] {
  const searchStart = Math.max(0, view.byteLength - EOCD_MAX_SEARCH)
  let eocdOffset = -1
  for (let offset = view.byteLength - EOCD_MIN_LENGTH; offset >= searchStart; offset--) {
    if (view.getUint32(offset, true) === EOCD_SIGNATURE) {
      eocdOffset = offset
      break
    }
  }
  if (eocdOffset < 0)
    throw new Error('This file is not a valid zip archive.')

  const diskNumber = view.getUint16(eocdOffset + 4, true)
  const directoryDisk = view.getUint16(eocdOffset + 6, true)
  if (diskNumber !== 0 || directoryDisk !== 0)
    throw new Error('Multi-disk zip archives are not supported.')

  const entryCount = view.getUint16(eocdOffset + 10, true)
  const directorySize = view.getUint32(eocdOffset + 12, true)
  const directoryOffset = view.getUint32(eocdOffset + 16, true)
  if (directoryOffset + directorySize > eocdOffset)
    throw new Error('The zip archive is truncated.')
  if (entryCount > THEME_ARCHIVE_MAX_ENTRIES)
    throw new Error(`The archive contains too many files (max ${THEME_ARCHIVE_MAX_ENTRIES}).`)

  const decoder = new TextDecoder('utf-8')
  const entries: CentralDirectoryEntry[] = []
  let offset = directoryOffset
  for (let index = 0; index < entryCount; index++) {
    if (offset + CENTRAL_DIRECTORY_FIXED_LENGTH > view.byteLength
      || view.getUint32(offset, true) !== CENTRAL_DIRECTORY_SIGNATURE)
      throw new Error('The zip archive is corrupt.')

    const method = view.getUint16(offset + 10, true)
    const compressedSize = view.getUint32(offset + 20, true)
    const uncompressedSize = view.getUint32(offset + 24, true)
    const nameLength = view.getUint16(offset + 28, true)
    const extraLength = view.getUint16(offset + 30, true)
    const commentLength = view.getUint16(offset + 32, true)
    const localHeaderOffset = view.getUint32(offset + 42, true)

    if (compressedSize === ZIP64_SENTINEL || uncompressedSize === ZIP64_SENTINEL || localHeaderOffset === ZIP64_SENTINEL)
      throw new Error('Zip64 archives are not supported.')

    const nameStart = offset + CENTRAL_DIRECTORY_FIXED_LENGTH
    if (nameStart + nameLength > view.byteLength)
      throw new Error('The zip archive is corrupt.')
    const name = decoder.decode(new Uint8Array(view.buffer, view.byteOffset + nameStart, nameLength))

    if (isThemeEntryName(name)) {
      if (uncompressedSize > THEME_ARCHIVE_MAX_ENTRY_BYTES)
        throw new Error(`"${name}" exceeds the ${THEME_ARCHIVE_MAX_ENTRY_BYTES / 1024} KiB entry limit.`)
      entries.push({ name, method, compressedSize, uncompressedSize, localHeaderOffset })
    }

    offset = nameStart + nameLength + extraLength + commentLength
  }
  return entries
}

function readEntryData(view: DataView, entry: CentralDirectoryEntry): Uint8Array {
  const offset = entry.localHeaderOffset
  if (offset + LOCAL_HEADER_FIXED_LENGTH > view.byteLength
    || view.getUint32(offset, true) !== LOCAL_HEADER_SIGNATURE)
    throw new Error('The zip archive is corrupt.')

  const nameLength = view.getUint16(offset + 26, true)
  const extraLength = view.getUint16(offset + 28, true)
  const dataStart = offset + LOCAL_HEADER_FIXED_LENGTH + nameLength + extraLength
  if (dataStart + entry.compressedSize > view.byteLength)
    throw new Error('The zip archive is truncated.')

  return new Uint8Array(view.buffer, view.byteOffset + dataStart, entry.compressedSize)
}

async function inflateRaw(data: Uint8Array, maxBytes: number): Promise<Uint8Array> {
  const stream = new Blob([data as unknown as BlobPart]).stream().pipeThrough(new DecompressionStream('deflate-raw'))
  const reader = stream.getReader()
  const chunks: Uint8Array[] = []
  let total = 0
  for (;;) {
    const { done, value } = await reader.read()
    if (done)
      break
    total += value.byteLength
    if (total > maxBytes) {
      await reader.cancel()
      throw new Error('An archive entry exceeds the size limit.')
    }
    chunks.push(value)
  }
  const output = new Uint8Array(total)
  let offset = 0
  for (const chunk of chunks) {
    output.set(chunk, offset)
    offset += chunk.byteLength
  }
  return output
}

/**
 * Reads every `.json` entry in a theme package archive. Returns the entries in
 * archive order; an archive without JSON entries resolves to an empty list.
 * Throws an `Error` with an English reason when the container itself is
 * invalid (not a zip, truncated, unsupported compression, over the caps).
 */
export async function readThemeArchiveEntries(data: ArrayBuffer): Promise<ThemeArchiveEntry[]> {
  const view = new DataView(data)
  const centralDirectory = readCentralDirectory(view)

  const decoder = new TextDecoder('utf-8')
  const entries: ThemeArchiveEntry[] = []
  let totalBytes = 0
  for (const entry of centralDirectory) {
    const compressed = readEntryData(view, entry)
    let content: Uint8Array
    if (entry.method === METHOD_STORE)
      content = compressed
    else if (entry.method === METHOD_DEFLATE)
      content = await inflateRaw(compressed, THEME_ARCHIVE_MAX_ENTRY_BYTES + 1)
    else
      throw new Error(`"${entry.name}" uses an unsupported compression method.`)

    if (content.byteLength !== entry.uncompressedSize)
      throw new Error(`"${entry.name}" is corrupt (size mismatch).`)

    totalBytes += content.byteLength
    if (totalBytes > THEME_ARCHIVE_MAX_TOTAL_BYTES)
      throw new Error(`The archive exceeds the ${THEME_ARCHIVE_MAX_TOTAL_BYTES / 1024 / 1024} MiB total limit.`)

    entries.push({ name: entry.name, text: decoder.decode(content) })
  }
  return entries
}
