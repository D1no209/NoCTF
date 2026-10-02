<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useRuntimeFlagsPanel } from './useRuntimeFlagsPanel'
import View from '~/components/views/admin/RuntimeFlagsPanelView.vue'

const props = withDefaults(defineProps<{ runtimeId: string; autoLoad?: boolean }>(), { autoLoad: false })
const state = bindViewState(useRuntimeFlagsPanel(props))
</script>

<template>
  <View :state="state" />
</template>
