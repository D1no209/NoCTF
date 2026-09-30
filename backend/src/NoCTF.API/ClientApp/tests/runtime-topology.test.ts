import { expect, test } from 'bun:test'
import { runtimeTopology } from '../app/features/admin/runtime-topology'
import { layoutConnections } from '../app/components/ui/connection-canvas/connection-layout'
import { emptyRuntimeService } from '../app/utils/game-config'
import type { ContainerDefinitionModel, UrlBindingModel } from '../app/utils/game-config'

const labels = {
  unnamedService: (index: number) => `Service ${index}`,
  missingImage: 'No image', missingService: 'No service', controlEntry: 'Control', noEntries: 'No entries',
}
const binding = (name: string, port: number): UrlBindingModel => ({
  serviceName: name, containerPort: port, exposure: 0, urlTemplate: 'tcp://{HOST}:{PORT}',
})

test('topology connects only explicit access entries and the separate control entry', () => {
  const definition: ContainerDefinitionModel = { kind: 'container', services: [emptyRuntimeService('web'), emptyRuntimeService('db')] }
  definition.services[1]!.internalPorts = [5432]
  const graph = runtimeTopology(definition, [binding('web', 80), binding('web', 443)], binding('db', 5432), labels)
  expect(graph.edges).toEqual([
    { source: 'service-0', target: 'entry-0' },
    { source: 'service-0', target: 'entry-1' },
    { source: 'service-1', target: 'entry-2' },
  ])
  expect(graph.nodes.find(node => node.id === 'entry-2')?.detail).toStartWith('Control')
  const withoutEntries = runtimeTopology(definition, [], null, labels)
  expect(withoutEntries.edges).toHaveLength(0)
  expect(withoutEntries.nodes.filter(node => node.column === 1)).toEqual([{ id: 'no-entries', column: 1, label: 'No entries' }])
})

test('dangling service references remain visible without fabricating a connection', () => {
  const graph = runtimeTopology({ kind: 'container', services: [emptyRuntimeService('web')] }, [binding('deleted', 8080)], null, labels)
  expect(graph.edges).toHaveLength(0)
  expect(graph.nodes.find(node => node.id === 'entry-0')).toMatchObject({ label: 'deleted:8080', invalid: true })
})

test('all 64 services remain non-overlapping and reachable on a narrow canvas', () => {
  const graph = runtimeTopology({ kind: 'container', services: Array.from({ length: 64 }, (_, index) => emptyRuntimeService(`service-${index}`)) }, [], null, labels)
  const layout = layoutConnections(graph.nodes, 320)
  const services = layout.nodes.filter(node => node.column === 0)
  expect(layout.width).toBeGreaterThanOrEqual(480)
  expect(services).toHaveLength(64)
  services.forEach((node, index) => {
    expect(node.y + node.height).toBeLessThanOrEqual(layout.height)
    expect(node.x + node.width).toBeLessThan(layout.width)
    if (index > 0) expect(node.y).toBeGreaterThan(services[index - 1]!.y + services[index - 1]!.height)
  })
})
