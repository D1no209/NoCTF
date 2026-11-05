<script setup lang="ts">
import { defineFeatureView } from '~/features/shared/define-feature-view'
import type { DefinitionModel } from '~/utils/game-config'

import { useDefinitionCheckerSection } from './useDefinitionCheckerSection'
import View from '~/components/views/admin/DefinitionCheckerSectionView.vue'

const props = withDefaults(defineProps<{
  model: DefinitionModel
  disabled?: boolean
}>(), {
  disabled: false,
})
const FeatureView = defineFeatureView(View, () => useDefinitionCheckerSection(props))

</script>

<template>
  <FeatureView />
</template>
