<script setup lang="ts">
import { Skeleton } from '@/components/ui/skeleton'
import AwdpScreenShell from '@/components/awdp-screen/AwdpScreenShell.vue'
</script>

<template>
  <AwdpScreenShell>
    <Skeleton class="h-[5.2rem] border border-slate-500/15 bg-slate-900/70" />
    <div class="grid grid-cols-8 gap-2">
      <Skeleton v-for="index in 8" :key="index" class="h-[4.5rem] border border-slate-500/15 bg-slate-900/70" />
    </div>
    <div class="grid min-h-0 grid-cols-[minmax(15.5rem,0.72fr)_minmax(0,1.65fr)_minmax(17rem,0.82fr)] gap-3">
      <Skeleton class="border border-slate-500/15 bg-slate-900/70" />
      <Skeleton class="border border-slate-500/15 bg-slate-900/70" />
      <Skeleton class="border border-slate-500/15 bg-slate-900/70" />
    </div>
    <div class="grid min-h-0 grid-cols-1 gap-3">
      <Skeleton class="border border-slate-500/15 bg-slate-900/70" />
    </div>
  </AwdpScreenShell>
</template>
