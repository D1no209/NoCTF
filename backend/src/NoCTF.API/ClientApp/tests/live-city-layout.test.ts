import { describe, expect, test } from 'bun:test'
import { PerspectiveCamera, Vector3 } from 'three'
import {
  fitLiveCityFrame, LIVE_CITY_FOV, LIVE_CITY_LABEL_OFFSET,
  liveCityBuildingHeight, liveCityPixelRatio, liveCitySlots,
} from '../app/lib/live-city-layout'

describe('CTF city responsive framing', () => {
  test('grows a bounded skyline as more challenges enter the overview', () => {
    expect(liveCityBuildingHeight(500, 500, 2)).toBe(28)
    expect(liveCityBuildingHeight(500, 500, 9)).toBeGreaterThan(42)
    expect(liveCityBuildingHeight(500, 500, 16)).toBeGreaterThan(50)
    expect(liveCityBuildingHeight(500, 500, 1000)).toBe(64)
    expect(liveCityBuildingHeight(0, 500, 16)).toBeGreaterThan(27)
    expect(liveCityBuildingHeight(-50, 500, 16)).toBe(liveCityBuildingHeight(0, 500, 16))
    expect(liveCityBuildingHeight(250, 500, 16)).toBeLessThan(liveCityBuildingHeight(500, 500, 16))
    expect(liveCityBuildingHeight(Number.NaN, 0, 1)).toBeGreaterThan(0)
  })

  test('assigns each challenge a distinct compact slot, highest scores nearest the center', () => {
    expect(liveCitySlots(0)).toEqual([])
    for (const count of [1, 2, 8, 16, 25, 64]) {
      const slots = liveCitySlots(count)
      expect(slots).toHaveLength(count)
      expect(new Set(slots.map(p => `${p.x}:${p.z}`)).size).toBe(count)
      const radii = slots.map(p => Math.hypot(p.x, p.z))
      expect(radii).toEqual([...radii].sort((a, b) => a - b))
    }
  })

  test('keeps multi-challenge buildings visibly taller than the old fixed-height overview', () => {
    for (const count of [9, 16, 36]) {
      const slots = liveCitySlots(count)
      const worldRadius = Math.max(...slots.map(p => Math.hypot(p.x, p.z) + 10.9))
      const buildingHeight = liveCityBuildingHeight(500, 500, count)
      const frame = fitLiveCityFrame(worldRadius, buildingHeight + 1.1 + LIVE_CITY_LABEL_OFFSET, 1440, 936)
      const camera = new PerspectiveCamera(LIVE_CITY_FOV, 1440 / 936, 0.1, frame.far)
      const projectedHeight = (radius: number, height: number, lookY: number, tower: number) => {
        camera.position.set(Math.sin(0.7) * radius, height, Math.cos(0.7) * radius)
        camera.lookAt(0, lookY, 0)
        camera.updateMatrixWorld(true)
        return new Vector3(0, tower, 0).project(camera).y - new Vector3(0, 0, 0).project(camera).y
      }
      const oldSpan = Math.ceil(Math.sqrt(count)) * 22
      const previous = projectedHeight(oldSpan * 1.1 + 36, oldSpan * 1.1 + 42, Math.min(7, oldSpan * 0.06 + 2.5), 29.1)
      const current = projectedHeight(frame.radius, frame.height, frame.lookY, buildingHeight + 1.1)
      expect(current).toBeGreaterThan(previous * 1.3)
    }
  })

  // These are arena sizes after the header/rail/footer, not the entire browser window.
  for (const [width, height] of [[960, 590], [1440, 920], [2016, 1280], [3280, 1940], [310, 460], [2200, 650]]) {
    test(`fits podiums, roofs and labels throughout a full orbit at ${width} × ${height}`, () => {
      for (const count of [1, 2, 8, 16, 36, 64]) {
        const slots = liveCitySlots(count)
        const worldRadius = Math.max(16, ...slots.map(p => Math.hypot(p.x, p.z) + 10.9))
        const worldHeight = liveCityBuildingHeight(500, 500, count) + 1.1 + LIVE_CITY_LABEL_OFFSET
        const frame = fitLiveCityFrame(worldRadius, worldHeight, width!, height!)
        const camera = new PerspectiveCamera(LIVE_CITY_FOV, width! / height!, 0.1, frame.far)
        const limitX = 1 - 2 * Math.min(width! * 0.4, 132) / width!
        const limitY = 1 - 2 * Math.min(height! * 0.4, 100) / height!
        let maxX = 0
        let maxY = 0
        for (let angle = 0; angle < Math.PI * 2; angle += Math.PI / 12) {
          camera.position.set(Math.sin(angle) * frame.radius, frame.height, Math.cos(angle) * frame.radius)
          camera.lookAt(0, frame.lookY, 0)
          camera.updateMatrixWorld(true)
          for (const p of slots) {
            for (const y of [0, worldHeight]) {
              for (const dx of [-7.7, 7.7]) {
                for (const dz of [-7.7, 7.7]) {
                  const point = new Vector3(p.x + dx, y, p.z + dz).project(camera)
                  maxX = Math.max(maxX, Math.abs(point.x))
                  maxY = Math.max(maxY, Math.abs(point.y))
                  expect(point.z).toBeLessThan(1)
                }
              }
            }
          }
        }
        expect(maxX).toBeLessThanOrEqual(limitX)
        expect(maxY).toBeLessThanOrEqual(limitY)
      }
    })
  }

  test('reframes narrow arenas and preserves framing for proportionally scaled viewports', () => {
    const wide = fitLiveCityFrame(70, 60, 1440, 900, 200, 80)
    const narrow = fitLiveCityFrame(70, 60, 400, 900, 200, 80)
    expect(narrow.radius).toBeGreaterThan(wide.radius)
    const larger = fitLiveCityFrame(70, 60, 2880, 1800, 440, 256)
    expect(larger.radius).toBeCloseTo(wide.radius, 5)
    expect(fitLiveCityFrame(16, 34, 0, 0).radius).toBeFinite()
  })

  test('focus distance is relative to the selected building, not its distance from city center', () => {
    const frame = fitLiveCityFrame(9.2, 60, 1440, 900)
    const camera = new PerspectiveCamera(LIVE_CITY_FOV, 1440 / 900, 0.1, frame.far)
    const projectedHeights: number[] = []
    for (const [x, z] of [[0, 0], [150, 120], [-120, -90]]) {
      const azimuth = Math.atan2(x!, z!)
      camera.position.set(x! + Math.sin(azimuth) * frame.radius, frame.height, z! + Math.cos(azimuth) * frame.radius)
      camera.lookAt(x!, frame.lookY, z!)
      camera.updateMatrixWorld(true)
      const roof = new Vector3(x!, 60, z!).project(camera)
      const foot = new Vector3(x!, 0, z!).project(camera)
      expect(Math.abs(roof.y)).toBeLessThan(1)
      expect(Math.abs(foot.y)).toBeLessThan(1)
      projectedHeights.push(roof.y - foot.y)
    }
    expect(projectedHeights[0]!).toBeCloseTo(projectedHeights[1]!, 6)
    expect(projectedHeights[0]!).toBeCloseTo(projectedHeights[2]!, 6)
    expect(fitLiveCityFrame(9.2, 60, 310, 460).radius).toBeGreaterThan(frame.radius)
  })

  test('compact labels leave more screen area for towers in a dense, narrow overview', () => {
    const full = fitLiveCityFrame(85, 70, 336, 503, 224, 104)
    const compact = fitLiveCityFrame(85, 70, 336, 503, 80, 42)
    expect(compact.radius).toBeLessThan(full.radius * 0.65)
  })

  test('bounds rendering resolution while responding to high-DPI and 4K resize', () => {
    expect(liveCityPixelRatio(1280, 720, 1)).toBe(1)
    expect(liveCityPixelRatio(1920, 1080, 2)).toBe(1.75)
    expect(liveCityPixelRatio(3840, 2160, 2)).toBe(1)
    const ratio = liveCityPixelRatio(7680, 4320, 2)
    expect(7680 * 4320 * ratio ** 2).toBeLessThanOrEqual(8_294_400)
  })
})
