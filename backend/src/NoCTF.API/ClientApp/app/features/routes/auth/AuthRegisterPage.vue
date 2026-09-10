<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useAuthRegisterPage } from './useAuthRegisterPage'
import View from '~/components/views/page/auth/AuthRegisterPageView.vue'

const state = bindViewState(useAuthRegisterPage())
</script>

<template>
  <View :state="state" />
</template>
