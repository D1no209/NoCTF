<script setup lang="ts">
import LiveSoloQuestionPool from '~/features/live-solo/LiveSoloQuestionPool.vue'
definePageMeta({ middleware: ['auth'] })
</script>
<template><LiveSoloQuestionPool /></template>
