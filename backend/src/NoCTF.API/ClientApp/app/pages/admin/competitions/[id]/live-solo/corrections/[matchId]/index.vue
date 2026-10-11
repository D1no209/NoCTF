<script setup lang="ts">
import LiveSoloCorrections from '~/features/live-solo/LiveSoloCorrections.vue'
definePageMeta({ middleware: 'competition-admin', key: route => route.fullPath })
</script>
<template><LiveSoloCorrections /></template>
