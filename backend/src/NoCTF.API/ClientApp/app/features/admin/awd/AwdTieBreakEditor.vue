<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import type { AwdTieBreak } from '~/utils/awd-tie-breaks'
import { useAwdTieBreakEditor, type AwdTieBreakEditorProps } from './useAwdTieBreakEditor'
import View from '~/components/views/admin/awd/AwdTieBreakEditorView.vue'
const props = defineProps<AwdTieBreakEditorProps>()
const emit = defineEmits<{ 'update:modelValue': [value: AwdTieBreak[]] }>()
const state = bindViewState(useAwdTieBreakEditor(props, emit))
</script>

<template><View :state="state" /></template>
