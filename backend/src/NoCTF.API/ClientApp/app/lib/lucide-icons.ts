import { defineAsyncComponent, type Component } from 'vue'
import { Flag } from '@lucide/vue'
import { normalizeLucideIconName } from './lucide-icon-name'

// Each selected icon is loaded separately; the full Lucide registry stays out of the entry bundle.
const modules = import.meta.glob<{ default: Component }>(['../../node_modules/@lucide/vue/dist/esm/icons/*.mjs', '!../../node_modules/@lucide/vue/dist/esm/icons/index.mjs'])
const components = new Map<string, Component>()
export function lucideIcon(value: string): Component {
  const name = normalizeLucideIconName(value)
  const load = modules[`../../node_modules/@lucide/vue/dist/esm/icons/${name}.mjs`]
  if (!load) return Flag
  let component = components.get(name)
  if (!component) {
    component = defineAsyncComponent({ loader: async () => (await load()).default, errorComponent: Flag })
    components.set(name, component)
  }
  return component
}
