/** World-space dimensions and viewport fitting shared by the CTF city and its tests. */
export const LIVE_CITY_CELL_SIZE = 22
export const LIVE_CITY_FOV = 52
export const LIVE_CITY_ELEVATION = Math.PI * 32 / 180
export const LIVE_CITY_LABEL_OFFSET = 4.6

export function liveCityBuildingHeight(score: number, maxScore: number, count: number): number {
  // More blocks require a wider overview. Grow the skyline moderately, not indefinitely.
  const skyline = 28 + Math.min(36, Math.max(0, Math.sqrt(count) - Math.sqrt(2)) * 9)
  const ratio = Number.isFinite(score) && maxScore > 0 ? Math.max(0, Math.min(1, score / maxScore)) : 0
  return skyline * (0.55 + ratio * 0.45)
}

export function liveCitySlots(count: number): { x: number; z: number }[] {
  const cols = Math.max(1, Math.ceil(Math.sqrt(count)))
  const rows = Math.max(1, Math.ceil(count / cols))
  return Array.from({ length: rows * cols }, (_, index) => ({
    x: (index % cols - (cols - 1) / 2) * LIVE_CITY_CELL_SIZE,
    z: (Math.floor(index / cols) - (rows - 1) / 2) * LIVE_CITY_CELL_SIZE,
  }))
    .sort((a, b) => Math.hypot(a.x, a.z) - Math.hypot(b.x, b.z))
    .slice(0, count)
}

export interface LiveCityFrame {
  radius: number
  height: number
  lookY: number
  far: number
}

/**
 * Fit a vertical cylinder at every orbit angle. Bounds include podiums and label anchors;
 * pixel gutters reserve the CSS labels (which do not scale with perspective) and arena HUD.
 */
export function fitLiveCityFrame(
  worldRadius: number,
  worldHeight: number,
  width: number,
  height: number,
  labelWidth = 224,
  labelHeight = 104,
): LiveCityFrame {
  const viewportWidth = Math.max(1, width)
  const viewportHeight = Math.max(1, height)
  const gutterX = Math.min(viewportWidth * 0.4, labelWidth / 2 + 20)
  const gutterY = Math.min(viewportHeight * 0.4, labelHeight / 2 + 48)
  const tanFov = Math.tan(LIVE_CITY_FOV * Math.PI / 360)
  const tanV = tanFov * (1 - 2 * gutterY / viewportHeight)
  const tanH = tanFov * viewportWidth / viewportHeight * (1 - 2 * gutterX / viewportWidth)
  const sin = Math.sin(LIVE_CITY_ELEVATION)
  const cos = Math.cos(LIVE_CITY_ELEVATION)
  const halfHeight = worldHeight / 2
  const horizontal = worldRadius * Math.hypot(1 / tanH, cos) + halfHeight * sin
  const top = worldRadius * Math.abs(cos - sin / tanV) + halfHeight * (sin + cos / tanV)
  const bottom = worldRadius * (cos + sin / tanV) + halfHeight * Math.abs(sin - cos / tanV)
  const distance = Math.max(72, horizontal, top, bottom) * 1.04
  return {
    radius: distance * cos,
    height: halfHeight + distance * sin,
    lookY: halfHeight,
    far: Math.max(600, distance + worldRadius * 2 + worldHeight + 100),
  }
}

export function liveCityPixelRatio(width: number, height: number, devicePixelRatio: number): number {
  // Keep 4K crisp without allocating a 16K-sized drawing buffer on high-DPI screens.
  return Math.min(Math.max(1, devicePixelRatio), 1.75, Math.sqrt(8_294_400 / Math.max(1, width * height)))
}
