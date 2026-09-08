import { describe, expect, test } from 'bun:test'
import { computed, effect, reactive, ref, stop, toRefs } from 'vue'
import { bindViewState } from '../app/features/shared/view-state'

describe('feature rendering boundary', () => {
  test('form input and commands share the same feature state', () => {
    const name = ref('before')
    const saving = ref(false)
    const submitted: string[] = []
    const state = bindViewState({ name, saving, submit: () => submitted.push(name.value) })
    const view = toRefs(state)
    view.name.value = 'after'
    view.submit.value()
    expect(name.value).toBe('after')
    expect(submitted).toEqual(['after'])
    saving.value = true
    expect(view.saving.value).toBe(true)
  })

  test('keeps nested injected refs intact and observes replacement props', () => {
    const context = { competition: ref({ id: 'first' }) }
    const props = reactive({ title: 'before' })
    const state = bindViewState({ context, ...toRefs(props), titleLength: computed(() => props.title.length) })
    expect(state.context.competition).toBe(context.competition)
    const rendered: string[] = []
    const observer = effect(() => rendered.push(`${state.title}:${state.titleLength}`))
    props.title = 'after'
    expect(rendered.at(-1)).toBe('after:5')
    stop(observer)
  })
})
