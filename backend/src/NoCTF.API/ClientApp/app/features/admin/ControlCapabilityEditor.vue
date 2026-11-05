<script setup lang="ts">
import { defineFeatureView } from '~/features/shared/define-feature-view'
import type { DefinitionModel } from '~/utils/game-config'
import { useControlCapabilityEditor } from './useControlCapabilityEditor'
import View from '~/components/views/admin/ControlCapabilityEditorView.vue'
const props = defineProps<{ model: DefinitionModel; disabled: boolean }>()
const FeatureView = defineFeatureView(View, () => useControlCapabilityEditor(props))
</script>
<template><FeatureView /></template>
