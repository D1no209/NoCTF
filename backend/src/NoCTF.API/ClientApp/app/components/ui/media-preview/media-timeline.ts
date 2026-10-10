export interface MediaFragmentRange { url: string; start: number; end: number }
export function fragmentAtPlaybackTime(ranges: Iterable<MediaFragmentRange>, currentTime: number, hasPlayed: boolean): string | null {
  if (!hasPlayed || !Number.isFinite(currentTime)) return null
  return [...ranges].find(range => range.start <= currentTime && currentTime < range.end)?.url ?? null
}
