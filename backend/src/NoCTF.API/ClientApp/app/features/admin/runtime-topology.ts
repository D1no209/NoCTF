import type { ConnectionEdge, ConnectionNode } from '../../components/ui/connection-canvas/connection-layout'
import type { ContainerDefinitionModel, RuntimeTemplateModel } from '../../utils/game-config'

export function runtimeTopology(
  definition: ContainerDefinitionModel,
  bindings: RuntimeTemplateModel['urlBindings'],
  controlBinding: RuntimeTemplateModel['controlCheckUrlBinding'],
  labels: { unnamedService: (index: number) => string; missingImage: string; missingService: string; controlEntry: string; noEntries: string },
) {
  const nodes: ConnectionNode[] = definition.services.map((service, index) => ({
    id: `service-${index}`,
    column: 0,
    label: service.name || labels.unnamedService(index + 1),
    detail: service.image || labels.missingImage,
    selectable: true,
  }))
  const edges: ConnectionEdge[] = []
  const entries = [...bindings, ...(controlBinding ? [controlBinding] : [])]
  entries.forEach((binding, index) => {
    const serviceIndex = definition.services.findIndex(service => service.name === binding.serviceName)
    const id = `entry-${index}`
    nodes.push({
      id,
      column: 1,
      label: `${binding.serviceName || labels.missingService}:${binding.containerPort ?? '—'}`,
      detail: index >= bindings.length ? `${labels.controlEntry} · ${binding.urlTemplate}` : binding.urlTemplate,
      invalid: serviceIndex < 0,
    })
    if (serviceIndex >= 0) edges.push({ source: `service-${serviceIndex}`, target: id })
  })
  if (entries.length === 0) nodes.push({ id: 'no-entries', column: 1, label: labels.noEntries })
  return { nodes, edges }
}
