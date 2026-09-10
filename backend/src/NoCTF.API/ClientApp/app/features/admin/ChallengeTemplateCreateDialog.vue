<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useChallengeTemplateCreateDialog } from './useChallengeTemplateCreateDialog'
import View from '~/components/views/admin/ChallengeTemplateCreateDialogView.vue'

const props = defineProps<{ open: boolean }>()
const emit = defineEmits<{
  'update:open': [value: boolean]
  'created': [templateId: string]
}>()
const state = bindViewState(useChallengeTemplateCreateDialog(props, emit))
</script>

<template>
  <View :state="state" />
</template>
