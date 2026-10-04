import { ref, toRaw, watch } from 'vue'
import type { DefinitionModel, GameModeValue } from '../utils/game-config'
import type { NoCTFAPIEndpointsAdministrationChallengeBankChallengeDefinitionContract } from '../api/models'
import { definitionContractToModel, definitionModelToContract } from '../utils/game-config'

/**
 * OpenAPI 强类型 definition 与编辑器 DefinitionModel 的映射。
 */
export function useDefinitionModel(
  modelValue: () => NoCTFAPIEndpointsAdministrationChallengeBankChallengeDefinitionContract,
  mode: () => GameModeValue,
  emit: (definition: NoCTFAPIEndpointsAdministrationChallengeBankChallengeDefinitionContract) => void,
) {
  const model = ref<DefinitionModel | null>(null)
  const parseFailed = ref(false)
  let syncing = false
  let lastEmitted: NoCTFAPIEndpointsAdministrationChallengeBankChallengeDefinitionContract | null = null
  let lastEmittedMode: GameModeValue | null = null

  watch(
    [modelValue, mode],
    ([definition, currentMode]) => {
      if (toRaw(definition) === lastEmitted && currentMode === lastEmittedMode) return
      syncing = true
      try {
        model.value = definitionContractToModel(definition, currentMode)
        parseFailed.value = false
      }
      catch {
        model.value = null
        parseFailed.value = true
      }
      finally {
        syncing = false
      }
    },
    { immediate: true, flush: 'sync' },
  )

  function emitDefinition(): void {
    if (!model.value || syncing) return
    lastEmittedMode = mode()
    lastEmitted = definitionModelToContract(lastEmittedMode, model.value)
    emit(lastEmitted)
  }

  watch(model, emitDefinition, { deep: true, flush: 'sync' })

  return { model, parseFailed }
}
