import { defineComponent, h, markRaw } from 'vue'
import type { Component } from 'vue'
import { bindViewState } from './view-state'

/** Vue accepts a slots object as component children; the public type does not name it. */
type ComponentChildren = Parameters<typeof h>[2]

/**
 * Wires one feature controller to its View and forwards every slot, so
 * composition entries keep only their typed props/emits contract instead of
 * repeating the bind/render/slot-forward template.
 */
export function defineFeatureView<TState extends object>(
  view: Component,
  useController: () => TState,
) {
  return markRaw(defineComponent({
    name: 'FeatureView',
    setup(_, { slots }) {
      const state = bindViewState(useController())
      return () => h(view, { state }, slots as unknown as ComponentChildren)
    },
  }))
}
