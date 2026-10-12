/** One shared team platform contains every service, with room between teams. */
export function awdTeamLayout(serviceCount: number) {
  const columns = Math.min(4, Math.max(1, serviceCount))
  const rows = Math.max(1, Math.ceil(serviceCount / columns))
  return {
    width: columns * .9 + .5,
    depth: rows * 1.1 + .6,
    services: Array.from({ length: serviceCount }, (_, index) => {
      const row = Math.floor(index / columns)
      const rowCount = Math.min(columns, serviceCount - row * columns)
      return { x: (index % columns - (rowCount - 1) / 2) * .9, y: 1, z: (row - (rows - 1) / 2) * 1.1 }
    }),
  }
}

/** Fit all team bounds through the perspective camera, including the near row. */
export function awdOverviewRadius(width: number, depth: number, aspect: number, angle: number) {
  const s = Math.sin(angle), c = Math.cos(angle), length = Math.hypot(1, .95)
  const tangent = Math.tan(39 * Math.PI / 360)
  let distance = 17 * length
  for (const x of [-width / 2, width / 2]) for (const z of [-depth / 2, depth / 2]) for (const y of [0, 2.1]) {
    const horizontal = x * c - z * s
    const vertical = (-.95 * (x * s + z * c) + y) / length
    const towardCamera = (x * s + z * c + y * .95) / length
    distance = Math.max(distance, towardCamera + Math.abs(horizontal) / (tangent * aspect * .88),
      towardCamera + Math.abs(vertical) / (tangent * .8))
  }
  return distance / length
}
