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
