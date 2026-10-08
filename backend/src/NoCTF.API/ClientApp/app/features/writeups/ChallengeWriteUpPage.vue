<script setup lang="ts">
import View from '~/components/views/writeups/ChallengeWriteUpPageView.vue'
import { bindViewState } from '~/features/shared/view-state'
import { useChallengeWriteUpPage } from './useChallengeWriteUpPage'
const props = defineProps<{ mine?: boolean }>()
const state = bindViewState(useChallengeWriteUpPage(props))
</script>
<template><View :state="state" /></template>
