<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useAccountPanel } from './useAccountPanel'
import View from '~/components/views/account/AccountPanelView.vue'

const state = bindViewState(useAccountPanel())
</script>

<template>
  <View :state="state" />
</template>
