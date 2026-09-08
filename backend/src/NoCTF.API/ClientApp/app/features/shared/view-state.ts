import { proxyRefs, shallowReactive } from 'vue'

/**
 * Bind feature-owned refs to a rendering surface without recursively unwrapping
 * nested contexts. Writable refs remain the form's model/event boundary.
 */
export function bindViewState<T extends object>(state: T) {
  return shallowReactive(proxyRefs(state))
}
