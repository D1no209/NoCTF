import type { DefinitionModel, GameModeValue } from '../utils/game-config'
import { parseDefinition, serializeDefinition } from '../utils/game-config'

/**
 * definitionJson 字符串 v-model 与结构化 DefinitionModel 的桥接。
 * model 是共享的响应式单一事实源,各定义切片组件直接修改它;
 * deep watch 统一序列化回 emit,自身序列化回灌时跳过重解析。
 */
export function useDefinitionModel(
  modelValue: () => string,
  mode: () => GameModeValue,
  emit: (json: string) => void,
) {
  const model = ref<DefinitionModel | null>(null)
  const parseFailed = ref(false)
  let lastSerialized = ''

  watch(
    modelValue,
    (json) => {
      // 自身序列化回灌跳过重解析。
      if (json === lastSerialized) return
      const parsed = parseDefinition(json, mode())
      if (parsed === null) {
        parseFailed.value = true
        model.value = null
        return
      }
      parseFailed.value = false
      model.value = parsed
      lastSerialized = json ?? ''
    },
    { immediate: true },
  )

  function emitSerialized(): void {
    if (!model.value) return
    const json = serializeDefinition(mode(), model.value)
    lastSerialized = json
    emit(json)
  }

  watch(model, emitSerialized, { deep: true })
  watch(mode, emitSerialized)

  return { model, parseFailed }
}
