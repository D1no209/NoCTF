<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useRuntimeAccessUrl } from './useRuntimeAccessUrl'
import View from '~/components/views/challenges/RuntimeAccessUrlView.vue'
import type { NoCtfapiEndpointsRuntimeRuntimeAccessResponse } from '~/api'

const props = defineProps<{ access: NoCtfapiEndpointsRuntimeRuntimeAccessResponse }>()
const state = bindViewState(useRuntimeAccessUrl(props))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
