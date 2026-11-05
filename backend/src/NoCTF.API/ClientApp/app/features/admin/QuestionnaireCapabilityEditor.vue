<script setup lang="ts">
import { defineFeatureView } from '~/features/shared/define-feature-view'
import type { DefinitionModel } from '~/utils/game-config'
import { useQuestionnaireCapabilityEditor } from './useQuestionnaireCapabilityEditor'
import View from '~/components/views/admin/QuestionnaireCapabilityEditorView.vue'
const props = defineProps<{ model: DefinitionModel; disabled: boolean; challengeId?: string; savedQuestionIds?: string[] }>()
const FeatureView = defineFeatureView(View, () => useQuestionnaireCapabilityEditor(props))
</script>
<template><FeatureView /></template>
