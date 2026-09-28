<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useAuthSsoCompletePage } from './useAuthSsoCompletePage'
import View from '~/components/views/page/auth/AuthSsoCompletePageView.vue'

const state = bindViewState(useAuthSsoCompletePage())
</script>

<template>
  <View :state="state" />
</template>
