import type { ConnectionEdge, ConnectionNode } from '../../components/ui/connection-canvas/connection-layout'
import type { ContainerDefinitionModel, RuntimeTemplateModel, DefinitionModel } from '../../utils/game-config'

export function runtimeTopology(
  definition: ContainerDefinitionModel,
  bindings: RuntimeTemplateModel['urlBindings'],
  controlBinding: DefinitionModel['control'],
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
  const entries = [...bindings]
  entries.forEach((binding, index) => {
    const serviceIndex = definition.services.findIndex(service => service.name === binding.serviceName)
    const id = `entry-${index}`
    nodes.push({
      id,
      column: 1,
      label: `${binding.serviceName || labels.missingService}:${binding.containerPort ?? '—'}`,
      detail: binding.urlTemplate,
      invalid: serviceIndex < 0,
    })
    if (serviceIndex >= 0) edges.push({ source: `service-${serviceIndex}`, target: id })
  })
  if (controlBinding) {
    const serviceIndex = definition.services.findIndex(service => service.name === controlBinding.serviceName)
    nodes.push({ id: 'control-job', column: 1, label: labels.controlEntry, detail: controlBinding.job.image || labels.missingImage, invalid: serviceIndex < 0 })
    if (serviceIndex >= 0) edges.push({ source: 'control-job', target: `service-${serviceIndex}` })
  }
  if (entries.length === 0 && !controlBinding) nodes.push({ id: 'no-entries', column: 1, label: labels.noEntries })
  return { nodes, edges }
}
