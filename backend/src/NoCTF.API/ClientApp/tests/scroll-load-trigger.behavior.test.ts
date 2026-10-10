import { expect, test } from 'bun:test'
import { effectScope, nextTick, reactive, ref, watch } from 'vue'

test('a visible load trigger coalesces requests, respects errors and disconnects its observer', async () => {
  const source = await Bun.file(new URL('../app/components/ui/scroll-area/ScrollLoadTrigger.vue', import.meta.url)).text()
  const script = source.match(/<script setup lang="ts">([\s\S]*?)<\/script>/)![1]!
  const compiled = new Bun.Transpiler({ loader: 'ts' }).transformSync(script)
  const props = reactive({ enabled: true, loading: false })
  let mount!: () => void, unmount!: () => void, observe!: (entries: any[]) => void
  let requests = 0, disconnected = false
  const scope = effectScope()
  const target = scope.run(() => new Function('deps', `const { ${['ref', 'watch', 'defineProps', 'withDefaults', 'defineEmits', 'onMounted', 'onBeforeUnmount', 'IntersectionObserver'].join(', ')} } = deps; ${compiled}; return target;`)({
    ref, watch, defineProps: () => props, withDefaults: (value: any) => value, defineEmits: () => () => { requests += 1 },
    onMounted: (fn: () => void) => { mount = fn }, onBeforeUnmount: (fn: () => void) => { unmount = fn },
    IntersectionObserver: class { constructor(callback: any) { observe = callback } observe() {} disconnect() { disconnected = true } },
  }))!
  target.value = { parentElement: { closest: () => ({}) } }
  mount()
  observe([{ isIntersecting: true }]); observe([{ isIntersecting: true }])
  expect(requests).toBe(1)
  props.loading = true; await nextTick()
  expect(requests).toBe(1)
  props.enabled = false; props.loading = false; await nextTick()
  expect(requests).toBe(1)
  props.enabled = true; await nextTick()
  expect(requests).toBe(1)
  observe([{ isIntersecting: false }])
  expect(requests).toBe(1)
  observe([{ isIntersecting: true }])
  expect(requests).toBe(2)
  unmount(); scope.stop()
  expect(disconnected).toBeTrue()
})
