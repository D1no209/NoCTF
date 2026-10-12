import { computed, markRaw, ref, type Ref } from 'vue'
import type { NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol } from '../../api'
import type { ConfigFieldDef, ConfigValues, GameModeValue } from '../../utils/game-config'
import { fieldDefaultValue } from '../../utils/game-config'
import { awdFieldVisible, awdInvalidFields, awdPresetValues, type AwdFieldGroup } from '../../utils/awd-config-fields'
import { translate } from '../../utils/i18n'
import DefinitionSectionComponent from '../../components/ui/section/DefinitionSection.vue'

/** Shared AWD form commands; views only render the resolved groups and preview. */
export function useAwdRuleEditor(
  props: { mode: GameModeValue; competitionStatus?: NoCtfapiEndpointsCompetitionsCompetitionStatusProtocol },
  fields: Ref<ConfigFieldDef[]>,
  effectiveValues: Ref<ConfigValues>,
  apply: (values: ConfigValues) => void,
) {
  const changeReason = ref('')
  const presetOpen = ref(false)
  const pendingPreset = ref<ConfigValues>({})
  const isAwd = computed(() => props.mode === 'Awd')
  const reasonRequired = computed(() => isAwd.value && ['Running', 'Paused', 'Finished'].includes(props.competitionStatus ?? ''))
  const invalidFields = computed(() => isAwd.value ? awdInvalidFields(fields.value, effectiveValues.value) : [])
  const canSaveRules = computed(() => invalidFields.value.length === 0 && (!reasonRequired.value || !!changeReason.value.trim()) && changeReason.value.trim().length <= 512)
  const groupOrder: AwdFieldGroup[] = ['timeline', 'scoring', 'flags', 'checker', 'sla', 'penalty', 'composition']
  const groups = computed(() => {
    if (!isAwd.value) return [{ key: '', title: '', collapsible: false, defaultOpen: true, fields: fields.value }]
    return groupOrder.map(key => {
      const members = fields.value.filter(field => field.group === key)
      return {
        key, title: translate(`administration.awd.group.${key}`),
        collapsible: ['flags', 'checker', 'sla', 'penalty', 'composition'].includes(key),
        defaultOpen: members.some(field => JSON.stringify(effectiveValues.value[field.key]) !== JSON.stringify(fieldDefaultValue(field))),
        fields: members.filter(field => awdFieldVisible(field, effectiveValues.value) || invalidFields.value.includes(field.key))
          .sort((a, b) => Number(b.key === 'preset') - Number(a.key === 'preset')),
      }
    }).filter(group => group.fields.length > 0)
  })
  function formatValue(field: ConfigFieldDef, value: unknown): string {
    if (value == null) return '—'
    const option = field.options?.find(option => option.value === value)
    return option ? translate(option.label) : String(value)
  }
  const presetChanges = computed(() => fields.value
    .filter(field => field.key in pendingPreset.value && effectiveValues.value[field.key] !== pendingPreset.value[field.key])
    .map(field => ({ key: field.key, label: field.label,
      before: formatValue(field, effectiveValues.value[field.key]), after: formatValue(field, pendingPreset.value[field.key]) })))
  function updateAwdField(key: string, value: unknown): boolean {
    if (!isAwd.value) return false
    if (key === 'preset') {
      pendingPreset.value = awdPresetValues(String(value))
      presetOpen.value = true
      return true
    }
    const field = fields.value.find(field => field.key === key)
    const changedScoring = field?.group && ['scoring', 'sla', 'penalty', 'composition'].includes(field.group)
    apply({ [key]: value, ...(changedScoring ? { preset: 'Custom' } : {}) })
    return true
  }
  function applyPreset() {
    const keys = new Set(fields.value.map(field => field.key))
    apply(Object.fromEntries(Object.entries(pendingPreset.value).filter(([key]) => keys.has(key))))
    presetOpen.value = false
  }
  function closePreset() { presetOpen.value = false }
  return { changeReason, isAwd, reasonRequired, invalidFields, canSaveRules, groups, presetOpen, presetChanges,
    updateAwdField, applyPreset, closePreset, DefinitionSection: markRaw(DefinitionSectionComponent) }
}
