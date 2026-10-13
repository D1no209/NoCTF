import { expect, test } from 'bun:test'
import { awdTeamLayout, awdOverviewRadius } from '../app/lib/spectator-awd-layout'
import { PerspectiveCamera, Vector3 } from 'three'

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

test('perspective overview includes near-row servers on narrow fields and rotated views', () => {
  for (const questions of [1, 3, 8]) for (const aspect of [1.2, 1.9, 3]) for (const angle of [0, .7, Math.PI / 2]) {
    const team = awdTeamLayout(questions), width = 6 * (team.width + 2.2) + team.width, depth = 4 * (team.depth + 2.8) + team.depth + 1
    const radius = awdOverviewRadius(width, depth, aspect, angle)
    const camera = new PerspectiveCamera(39, aspect, .1, 200)
    camera.position.set(Math.sin(angle) * radius, .95 * radius, Math.cos(angle) * radius)
    camera.lookAt(0, 0, 0); camera.updateMatrixWorld()
    for (const x of [-width / 2, width / 2]) for (const z of [-depth / 2, depth / 2]) for (const y of [0, 2.1]) {
      const point = new Vector3(x, y, z).project(camera)
      expect(Math.abs(point.x)).toBeLessThanOrEqual(.880001)
      expect(Math.abs(point.y)).toBeLessThanOrEqual(.800001)
    }
  }
})
