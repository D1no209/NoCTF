<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useAuthLoginPage } from './useAuthLoginPage'
import View from '~/components/views/page/auth/AuthLoginPageView.vue'

const state = bindViewState(useAuthLoginPage())
</script>

<template>
  <View :state="state" />
</template>
