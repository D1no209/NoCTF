import { expect, test } from 'bun:test'
import { Vector3 } from 'three'
import { createTerrain, terrainHeight } from '../app/lib/spectator-terrain'

const peaks = [new Vector3(-4.75, 0, -4.5), new Vector3(4.75, 0, -4.5), new Vector3(0, 0, 4.5)]

test('flag summits stay broad, finite and deterministic without any score or ownership input', () => {
  const peak = peaks[0]!, height = terrainHeight(peak.x, peak.z, peaks)
  expect(height).toBeGreaterThan(2.5)
  for (const dx of [-.25, 0, .25]) for (const dz of [-.25, 0, .25]) {
    const sample = terrainHeight(peak.x + dx, peak.z + dz, peaks)
    expect(Number.isFinite(sample)).toBe(true)
    expect(Math.abs(sample - height)).toBeLessThan(.15)
    expect(terrainHeight(peak.x + dx, peak.z + dz, peaks)).toBe(sample)
  }
})

test('far terrain covers the viewport without using dense summit geometry everywhere', () => {
  const terrain = createTerrain(peaks, 115, 114)
  const position = terrain.geometry.getAttribute('position')
  expect(position.count).toBeLessThan(400000)
  expect([...position.array].every(Number.isFinite)).toBe(true)
  expect(terrain.geometry.getAttribute('mesaMask').count).toBe(position.count)
  expect(terrain.contours.getAttribute('color').count).toBe(terrain.contours.getAttribute('position').count)
  terrain.geometry.computeBoundingBox()
  expect(terrain.geometry.boundingBox!.min.x).toBe(-57.5)
  expect(terrain.geometry.boundingBox!.max.z).toBe(57)
  terrain.geometry.dispose(); terrain.contours.dispose(); terrain.controlContours.forEach(g => g.dispose())
})

test('twelve-node overview bounds geometry while retaining the same deterministic terrain', () => {
  const nodes = Array.from({ length: 12 }, (_, i) => new Vector3((i % 4 - 1.5) * 9.5, 0, (Math.floor(i / 4) - 1) * 9))
  const terrain = createTerrain(nodes, 134, 123)
  expect(terrain.geometry.getAttribute('position').count).toBeLessThan(350000)
  for (const node of nodes) expect(terrainHeight(node.x, node.z, nodes)).toBeGreaterThan(2.5)
  terrain.geometry.dispose(); terrain.contours.dispose(); terrain.controlContours.forEach(g => g.dispose())
})
