import { computed, defineComponent, h, type VNodeChild } from 'vue'
import Hint from '../tooltip/Hint.vue'

/** Render already-sanitized markup, composing user-authored titles with our tooltip primitive. */
export default defineComponent({
  props: { html: { type: String, required: true } },
  setup(props) {
    const nodes = computed(() => {
      const body = new DOMParser().parseFromString(props.html, 'text/html').body
      function renderNode(node: Node): VNodeChild {
        if (node.nodeType === Node.TEXT_NODE) return node.textContent ?? ''
        if (!(node instanceof HTMLElement)) return null
        const attributes = Object.fromEntries(Array.from(node.attributes).filter(attr => attr.name !== 'title').map(attr => [attr.name, attr.value]))
        const children = Array.from(node.childNodes).map(renderNode)
        const title = node.getAttribute('title')
        if (!title) return h(node.tagName.toLowerCase(), attributes, children)
        if (!['A', 'BUTTON'].includes(node.tagName)) attributes.tabindex ??= '0'
        return h(Hint, { content: title }, { default: () => h(node.tagName.toLowerCase(), attributes, children) })
      }
      return Array.from(body.childNodes).map(renderNode)
    })
    return () => nodes.value
  },
})
