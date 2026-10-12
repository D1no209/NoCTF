import * as THREE from 'three'

function hash(x: number, z: number) {
  const value = Math.sin(x * 127.1 + z * 311.7) * 43758.5453
  return value - Math.floor(value)
}
function noise(x: number, z: number) {
  const ix = Math.floor(x), iz = Math.floor(z), fx = x - ix, fz = z - iz
  const u = fx * fx * (3 - 2 * fx), v = fz * fz * (3 - 2 * fz)
  return (hash(ix, iz) * (1 - u) + hash(ix + 1, iz) * u) * (1 - v)
    + (hash(ix, iz + 1) * (1 - u) + hash(ix + 1, iz + 1) * u) * v
}
function rockNoise(x: number, z: number) {
  return noise(x, z) * .55 + noise(x * 2.1, z * 2.1) * .28 + noise(x * 4.3, z * 4.3) * .12 + noise(x * 8.7, z * 8.7) * .05
}

/** Stable physical terrain: neither ownership nor points enter the height function. */
export function terrainHeight(x: number, z: number, peaks: THREE.Vector3[]) {
  const warpX = x + (rockNoise(x * .65, z * .65) - .5) * 1.7
  const warpZ = z + (rockNoise(z * .72 + 19, x * .72) - .5) * 1.5
  let mountain = 0
  for (const [i, peak] of peaks.entries()) {
    const angle = i * 1.73, dx = warpX - peak.x, dz = warpZ - peak.z
    const a = (dx * Math.cos(angle) + dz * Math.sin(angle)) / (3.35 + .55 * Math.sin(i * 2.7))
    const b = (-dx * Math.sin(angle) + dz * Math.cos(angle)) / (2.55 + .35 * Math.cos(i * 1.7))
    const radius = Math.pow(Math.abs(a) ** 2.4 + Math.abs(b) ** 2.4, 1 / 2.4)
    const envelope = Math.max(0, 1 - radius)
    const raw = Math.min(1, envelope * 1.6) * (3.25 + .65 * Math.sin(i * 2.1))
    const strata = Math.floor(raw / .32) * .32 + Math.min(.32, (raw % .32) * 4)
    mountain = Math.max(mountain, strata + envelope * (rockNoise(x * 1.5, z * 1.5) - .5) * .75)
  }
  const lowlands = Math.pow(rockNoise(x * .38 + 8, z * .38), 2) * 1.3
  const crags = (1 - Math.abs(noise(x * 2.5, z * 2.5) * 2 - 1)) * .17
  return .05 + mountain + lowlands + crags
}

export function createTerrain(peaks: THREE.Vector3[], width: number, depth: number) {
  const geometry = new THREE.PlaneGeometry(width, depth, Math.ceil(width * 9), Math.ceil(depth * 9))
  geometry.rotateX(-Math.PI / 2)
  const p = geometry.attributes.position!
  for (let i = 0; i < p.count; i++) p.setY(i, terrainHeight(p.getX(i), p.getZ(i), peaks))
  geometry.computeVertexNormals()
  // Intersect real terrain triangles with horizontal planes, not decorative circles.
  const segments: number[] = [], highlights: number[][] = peaks.map(() => []), index = geometry.index!
  const vertices = [new THREE.Vector3(), new THREE.Vector3(), new THREE.Vector3()]
  for (let i = 0; i < index.count; i += 3) {
    vertices.forEach((v, j) => v.fromBufferAttribute(p, index.getX(i + j)))
    const low = Math.min(...vertices.map(v => v.y)), high = Math.max(...vertices.map(v => v.y))
    for (let level = Math.max(.66, Math.ceil(low / .22) * .22); level < high; level += .22) {
      const hits: THREE.Vector3[] = []
      for (let j = 0; j < 3; j++) {
        const a = vertices[j]!, b = vertices[(j + 1) % 3]!
        if ((a.y <= level && b.y > level) || (b.y <= level && a.y > level))
          hits.push(a.clone().lerp(b, (level - a.y) / (b.y - a.y)))
      }
      if (hits.length === 2) {
        hits.forEach(v => segments.push(v.x, v.y + .018, v.z))
        if (level > 1 && Math.round(level / .22) % 4 === 0) {
          const center = hits[0]!
          const nearest = peaks.reduce((best, p, i) => Math.hypot(p.x - center.x, p.z - center.z) < Math.hypot(peaks[best]!.x - center.x, peaks[best]!.z - center.z) ? i : best, 0)
          hits.forEach(v => highlights[nearest]!.push(v.x, v.y + .028, v.z))
        }
      }
    }
  }
  const contours = new THREE.BufferGeometry()
  contours.setAttribute('position', new THREE.Float32BufferAttribute(segments, 3))
  const colors: number[] = [], stride = geometry.parameters.widthSegments + 1
  for (let i = 0; i < p.count; i++) {
    // Baked local cavity shading, rather than coarse per-vertex color noise.
    const neighbors = [i % stride > 0 ? i - 1 : i, i % stride < stride - 1 ? i + 1 : i, i - stride, i + stride]
      .filter(n => n >= 0 && n < p.count)
    const surrounding = neighbors.reduce((sum, n) => sum + p.getY(n), 0) / neighbors.length
    const cavity = Math.min(.38, Math.max(0, surrounding - p.getY(i)) * 1.8)
    const shade = .86 + rockNoise(p.getX(i) * .28, p.getZ(i) * .28) * .12 - cavity
    colors.push(shade, shade, shade)
  }
  geometry.setAttribute('color', new THREE.Float32BufferAttribute(colors, 3))
  const controlContours = highlights.map(values => new THREE.BufferGeometry().setAttribute('position', new THREE.Float32BufferAttribute(values, 3)))
  return { geometry, contours, controlContours }
}
