<script setup lang="ts">
import LiveSoloMatch from '~/features/live-solo/LiveSoloMatch.vue'
definePageMeta({ middleware: ['auth'], key: route => route.params.matchId as string })
</script>
<template><LiveSoloMatch /></template>
