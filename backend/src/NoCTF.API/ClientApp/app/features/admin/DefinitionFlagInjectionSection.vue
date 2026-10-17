<script setup lang="ts">
import { defineFeatureView } from '~/features/shared/define-feature-view'
import type { DefinitionModel } from '~/utils/game-config'
import { useDefinitionFlagInjectionSection } from './useDefinitionFlagInjectionSection'
import View from '~/components/views/admin/DefinitionFlagInjectionSectionView.vue'

const props = withDefaults(defineProps<{
  model: DefinitionModel
  disabled?: boolean
}>(), {
  disabled: false,
})
const FeatureView = defineFeatureView(View, () => useDefinitionFlagInjectionSection(props))

</script>

<template>
  <FeatureView />
</template>
