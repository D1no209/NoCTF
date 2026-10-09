<script setup lang="ts">
import LiveSoloCorrections from '~/features/live-solo/LiveSoloCorrections.vue'
definePageMeta({ middleware: ['auth'], key: route => route.fullPath })
</script>
<template><LiveSoloCorrections /></template>
