<script setup lang="ts">
import { defineFeatureView } from '~/features/shared/define-feature-view'
import type { DefinitionModel } from '~/utils/game-config'

import { useDefinitionPatchSection } from './useDefinitionPatchSection'
import View from '~/components/views/admin/DefinitionPatchSectionView.vue'

const props = withDefaults(defineProps<{
  model: DefinitionModel
  disabled?: boolean
}>(), {
  disabled: false,
})
const FeatureView = defineFeatureView(View, () => useDefinitionPatchSection(props))

</script>

<template>
  <FeatureView />
</template>
