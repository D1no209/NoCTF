<script setup lang="ts">
import { bindViewState } from '~/features/shared/view-state'
import { useAwdCheckReview, type AwdCheckReviewProps } from './useAwdCheckReview'
import View from '~/components/views/admin/awd/AwdCheckReviewView.vue'

const props = defineProps<AwdCheckReviewProps>()
const state = bindViewState(useAwdCheckReview(props))
</script>

<template><View :state="state" /></template>
