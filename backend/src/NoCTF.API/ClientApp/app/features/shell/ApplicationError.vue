<script setup lang="ts">
import type { NuxtError } from '#app'
import { bindViewState } from '~/features/shared/view-state'
import { useApplicationError } from './useApplicationError'
import View from '~/components/views/app/ApplicationErrorView.vue'

const props = defineProps<{ error: NuxtError }>()
const state = bindViewState(useApplicationError(props.error))
</script>

<template>
  <View :state="state" />
</template>
