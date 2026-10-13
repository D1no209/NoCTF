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

function angularNoise(x: number, z: number) {
  const ix = Math.floor(x), iz = Math.floor(z), fx = x - ix, fz = z - iz
  if (fx + fz < 1) return hash(ix, iz) + (hash(ix + 1, iz) - hash(ix, iz)) * fx + (hash(ix, iz + 1) - hash(ix, iz)) * fz
  return hash(ix + 1, iz + 1) + (hash(ix, iz + 1) - hash(ix + 1, iz + 1)) * (1 - fx)
    + (hash(ix + 1, iz) - hash(ix + 1, iz + 1)) * (1 - fz)
}

function smoothStep(low: number, high: number, value: number) {
  const t = Math.max(0, Math.min(1, (value - low) / (high - low)))
  return t * t * (3 - 2 * t)
}

/** Broad erosion terraces with steep scarps. The fixed topography has no score or owner input. */
export function terrainHeight(x: number, z: number, peaks: THREE.Vector3[]) {
  const warpX = (rockNoise(x * .23 + 11, z * .23) - .5) * 1.55
  const warpZ = (rockNoise(z * .21 + 39, x * .21) - .5) * 1.4
  let mountain = 0, distance = Infinity
  for (const [i, peak] of peaks.entries()) {
    const dx = x - peak.x, dz = z - peak.z
    distance = Math.min(distance, Math.hypot(dx, dz))
    if (Math.abs(dx) > 6.7 || Math.abs(dz) > 6.7) continue
    const angle = i * 1.13 + .3, c = Math.cos(angle), sn = Math.sin(angle)
    const ax = (dx + warpX) * c + (dz + warpZ) * sn
    const az = -(dx + warpX) * sn + (dz + warpZ) * c
    const a = ax / (4.75 + .25 * Math.sin(i * 2.7)), b = az / (3.95 + .3 * Math.cos(i * 1.7))
    const radial = Math.pow(Math.abs(a) ** 2.8 + Math.abs(b) ** 2.8, 1 / 2.8)
    const theta = Math.atan2(az, ax)
    const heights = [.55, 1.25, 2.3, 3.1], radii = [1, .8, .6, .33]
    const scale = .84 + .16 * (.5 + .5 * Math.sin(i * 1.7 + .5))
    const erosion = Math.max(0, noise(x * .56 + i * 13, z * .56) - .52) * .65 * smoothStep(.12, .4, radial)
    let mesa = .65 * scale * (1 - smoothStep(.65, 1.17, radial))
    for (let level = 1; level < heights.length; level++) {
      const outline = 1 + .095 * Math.sin(theta * 3 + i * 1.4 + level * .55)
        + .05 * Math.cos(theta * 5 - i + level * .85)
      const chipped = radial / outline + (angularNoise(x * 1.1 + level, z * 1.1) - .5) * .09 + erosion * (1 + level * .12)
        + Math.max(0, noise(x * .72 + level * 9, z * .72) - .65) * .22
      mesa = Math.max(mesa, heights[level]! * scale * (1 - smoothStep(radii[level]! - .025, radii[level]! + .025, chipped)))
    }
    if (mesa > 0) mesa += (angularNoise(x * 1.2, z * 1.2) - .5) * .085 + (rockNoise(x * .4, z * .4) - .5) * .065
    mountain = Math.max(mountain, mesa + .15)
  }
  // A connected landscape: quiet valley floors and angular peripheral ridges,
  // rather than a rectangular carpet of evenly spaced micro-bumps.
  const ridge = angularNoise((x + z * .4) * .29 + 61, (z - x * .3) * .29 + 8)
  const ridgeDetail = angularNoise((x - z * .6) * 1.15, (z + x * .5) * 1.15)
  const outer = smoothStep(3.4, 7.5, distance)
  const basin = Math.exp(-(((x + .7) / 2.7) ** 2) - ((z + 4.8) / 2.1) ** 2)
  const ground = .12 + rockNoise(x * .18, z * .18) * .23
    + (ridge ** 2 * 2.5 + ridgeDetail ** 2 * .85) * outer
  return Math.max(mountain, ground * (1 - basin * .86))
}

export function createTerrain(peaks: THREE.Vector3[], width: number, depth: number) {
  // The overview camera scales with node count. Keep comparable screen-space
  // detail without multiplying dense triangles for smaller, distant mesas.
  const summitStep = Math.min(.095, .055 * Math.max(1, Math.sqrt(peaks.length / 3)))
  const axis = (size: number, extent: number) => {
    const coordinates: number[] = [-size / 2]
    while (coordinates[coordinates.length - 1]! < size / 2) {
      const last = coordinates[coordinates.length - 1]!
      const step = Math.abs(last) < extent ? summitStep : Math.abs(last) < extent + 10 ? .22 : 1.1
      coordinates.push(Math.min(size / 2, last + step))
    }
    return coordinates
  }
  const xs = axis(width, Math.max(...peaks.map(p => Math.abs(p.x))) + 5.7)
  const zs = axis(depth, Math.max(...peaks.map(p => Math.abs(p.z))) + 5.7)
  const geometry = new THREE.PlaneGeometry(width, depth, xs.length - 1, zs.length - 1)
  geometry.rotateX(-Math.PI / 2)
  const p = geometry.attributes.position!
  for (let i = 0; i < p.count; i++) {
    const x = xs[i % xs.length]!, z = zs[Math.floor(i / xs.length)]!
    p.setXYZ(i, x, terrainHeight(x, z, peaks), z)
  }
  geometry.computeVertexNormals()
  // Intersect real terrain triangles with horizontal planes, not decorative circles.
  const segments: number[] = [], contourColors: number[] = [], highlights: number[][] = peaks.map(() => []), index = geometry.index!
  const contourWidth = Math.max(...peaks.map(p => Math.abs(p.x))) + 16, contourDepth = Math.max(...peaks.map(p => Math.abs(p.z))) + 16
  const vertices = [new THREE.Vector3(), new THREE.Vector3(), new THREE.Vector3()]
  for (let i = 0; i < index.count; i += 3) {
    vertices.forEach((v, j) => v.fromBufferAttribute(p, index.getX(i + j)))
    if (Math.abs(vertices[0]!.x) > contourWidth
      || Math.abs(vertices[0]!.z) > contourDepth) continue
    let nearest = 0, nearestDistance = Infinity
    peaks.forEach((peak, j) => {
      const distance = Math.hypot(peak.x - vertices[0]!.x, peak.z - vertices[0]!.z)
      if (distance < nearestDistance) { nearest = j; nearestDistance = distance }
    })
    const strength = .28 + .72 * (1 - smoothStep(4.5, 7.5, nearestDistance))
    const low = Math.min(vertices[0]!.y, vertices[1]!.y, vertices[2]!.y), high = Math.max(vertices[0]!.y, vertices[1]!.y, vertices[2]!.y)
    for (let level = Math.max(.3, Math.ceil(low / .15) * .15); level < high; level += .15) {
      const hits: THREE.Vector3[] = []
      for (let j = 0; j < 3; j++) {
        const a = vertices[j]!, b = vertices[(j + 1) % 3]!
        if ((a.y <= level && b.y > level) || (b.y <= level && a.y > level))
          hits.push(a.clone().lerp(b, (level - a.y) / (b.y - a.y)))
      }
      if (hits.length === 2) {
        hits.forEach(v => { segments.push(v.x, v.y + .018, v.z); contourColors.push(strength, strength, strength) })
        if (nearestDistance < 4.7 && level > .8 && Math.round(level / .15) % 5 === 0) {
          hits.forEach(v => highlights[nearest]!.push(v.x, v.y + .028, v.z))
        }
      }
    }
  }
  const contours = new THREE.BufferGeometry()
  contours.setAttribute('position', new THREE.Float32BufferAttribute(segments, 3))
  contours.setAttribute('color', new THREE.Float32BufferAttribute(contourColors, 3))
  const colors: number[] = [], mesaMasks: number[] = [], stride = geometry.parameters.widthSegments + 1
  for (let i = 0; i < p.count; i++) {
    // Baked local cavity shading, rather than coarse per-vertex color noise.
    const neighbors = [i % stride > 0 ? i - 1 : i, i % stride < stride - 1 ? i + 1 : i, i - stride, i + stride]
      .filter(n => n >= 0 && n < p.count)
    const surrounding = neighbors.reduce((sum, n) => sum + p.getY(n), 0) / neighbors.length
    const cavity = Math.min(.38, Math.max(0, surrounding - p.getY(i)) * 1.8)
    const shade = .86 + rockNoise(p.getX(i) * .28, p.getZ(i) * .28) * .12 - cavity
    colors.push(shade, shade, shade)
    const distance = Math.min(...peaks.map(peak => Math.hypot(peak.x - p.getX(i), peak.z - p.getZ(i))))
    mesaMasks.push(1 - smoothStep(4.2, 6.8, distance))
  }
  geometry.setAttribute('color', new THREE.Float32BufferAttribute(colors, 3))
  geometry.setAttribute('mesaMask', new THREE.Float32BufferAttribute(mesaMasks, 1))
  const controlContours = highlights.map(values => new THREE.BufferGeometry().setAttribute('position', new THREE.Float32BufferAttribute(values, 3)))
  return { geometry, contours, controlContours }
}

/** Static angular fragments break the perfect terrace rim and form talus below scarps. */
export function terrainFragments(peaks: THREE.Vector3[]) {
  const fragments: { position: THREE.Vector3; scale: THREE.Vector3; rotation: number; shade: number }[] = []
  peaks.forEach((peak, i) => {
    for (let n = 0; n < 240; n++) {
      const a = hash(n + 19, i + 8) * Math.PI * 2, r = 1.2 + hash(n + 7, i + 37) * 4.2
      const x = peak.x + Math.cos(a) * r, z = peak.z + Math.sin(a) * r
      const y = terrainHeight(x, z, peaks)
      const change = Math.max(Math.abs(terrainHeight(x + .18, z, peaks) - y), Math.abs(terrainHeight(x, z + .18, peaks) - y))
      if (y < .23 || (change < .12 && hash(n, i + 53) < .82)) continue
      const size = .05 + hash(n + 4, i) * .10
      fragments.push({ position: new THREE.Vector3(x, y - size * .08, z),
        scale: new THREE.Vector3(size * 1.6, change > .25 ? size * 1.1 : size * .3, size),
        rotation: a, shade: .72 + hash(n + 16, i) * .35 })
    }
  })
  return fragments
}
