<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'

import { useAvatarCropDialog } from './useAvatarCropDialog'
import View from '~/components/views/account/AvatarCropDialogView.vue'

const props = withDefaults(defineProps<{
  open: boolean
  file: File | null
  saving: boolean
  variant?: 'avatar' | 'profile-cover'
}>(), {
  variant: 'avatar',
})
const emit = defineEmits<{
  'update:open': [value: boolean]
  'save': [file: File]
  'error': [error: Error]
}>()
const state = bindViewState(useAvatarCropDialog(props, emit))

</script>

<template>
  <View :state="state">
    <template v-for="(_, name) in $slots" #[name]="slotProps"><slot :name="name" v-bind="slotProps || {}" /></template>
  </View>
</template>
