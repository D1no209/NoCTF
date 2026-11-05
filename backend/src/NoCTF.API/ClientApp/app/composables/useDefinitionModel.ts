import { ref, toRaw, watch } from 'vue'
import type { DefinitionModel } from '../utils/game-config'
import type { NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract } from '../api'
import { definitionContractToModel, definitionModelToContract } from '../utils/game-config'

/**
 * OpenAPI 强类型 definition 与编辑器 DefinitionModel 的映射。
 */
export function useDefinitionModel(
  modelValue: () => NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract,
  emit: (definition: NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract) => void,
) {
  const model = ref<DefinitionModel | null>(null)
  const parseFailed = ref(false)
  let syncing = false
  let lastEmitted: NoCtfapiEndpointsAdministrationChallengeBankChallengeDefinitionContract | null = null

  watch(
    modelValue,
    (definition) => {
      if (toRaw(definition) === lastEmitted) return
      syncing = true
      try {
        model.value = definitionContractToModel(definition)
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
    lastEmitted = definitionModelToContract(model.value)
    emit(lastEmitted)
  }

  watch(model, emitDefinition, { deep: true, flush: 'sync' })

  return { model, parseFailed }
}
