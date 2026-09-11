export function exceedsUploadLimit(
  byteLength: number,
  maximumBytes: number | null | undefined,
): boolean {
  return typeof maximumBytes === 'number'
    && Number.isSafeInteger(maximumBytes)
    && maximumBytes > 0
    && byteLength > maximumBytes
}
