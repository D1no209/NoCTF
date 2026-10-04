<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import type { NoCTFAPIEndpointsCompetitionsCompetitionResponse } from '~/api/models'
import { useCreateCompetitionDialog } from './useCreateCompetitionDialog'
import View from '~/components/views/competitions/CreateCompetitionDialogView.vue'

const props = defineProps<{ open: boolean }>()
const emit = defineEmits<{
  'update:open': [value: boolean]
  created: [competition: NoCTFAPIEndpointsCompetitionsCompetitionResponse]
}>()
const state = bindViewState(useCreateCompetitionDialog(props, emit))
</script>

<template>
  <View :state="state" />
</template>
