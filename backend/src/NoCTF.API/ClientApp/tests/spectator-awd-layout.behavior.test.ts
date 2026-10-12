import { expect, test } from 'bun:test'
import { awdTeamLayout } from '../app/lib/spectator-awd-layout'

test('all servers fit on their team platform, without overlapping even after adding rows', () => {
  for (const count of [0, 1, 8, 17, 32]) {
    const layout = awdTeamLayout(count)
    expect(layout.services).toHaveLength(count)
    for (const [index, server] of layout.services.entries()) {
      expect(Math.abs(server.x) + .33).toBeLessThan(layout.width / 2)
      expect(Math.abs(server.z) + .38).toBeLessThan(layout.depth / 2)
      for (const other of layout.services.slice(index + 1))
        expect(Math.abs(server.x - other.x) > .66 || Math.abs(server.z - other.z) > .76).toBe(true)
    }
    expect(awdTeamLayout(count)).toEqual(layout)
  }
})
