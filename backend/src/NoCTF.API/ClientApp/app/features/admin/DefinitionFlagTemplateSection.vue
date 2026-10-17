<script setup lang="ts">
import { defineFeatureView } from '~/features/shared/define-feature-view'
import type { DefinitionModel, GameModeValue } from '~/utils/game-config'

import { useDefinitionFlagTemplateSection } from './useDefinitionFlagTemplateSection'
import View from '~/components/views/admin/DefinitionFlagTemplateSectionView.vue'

const props = withDefaults(defineProps<{
  model: DefinitionModel
  mode: GameModeValue
  disabled?: boolean
}>(), {
  disabled: false,
})
const FeatureView = defineFeatureView(View, () => useDefinitionFlagTemplateSection(props))

</script>

<template>
  <FeatureView />
</template>
