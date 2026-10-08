<script setup lang="ts">
import View from '~/components/views/writeups/ChallengeWriteUpEditorView.vue'
import { bindViewState } from '~/features/shared/view-state'
import { useChallengeWriteUpEditor } from './useChallengeWriteUpEditor'
const props = defineProps<{ competitionId: string; competitionChallengeId: string; official?: boolean }>()
const emit = defineEmits<{ saved: [] }>()
const controller = useChallengeWriteUpEditor(props, () => emit('saved'))
const state = bindViewState(controller)
defineExpose({ confirmDiscard: controller.confirmDiscard })
</script>
<template><View :state="state" /></template>
