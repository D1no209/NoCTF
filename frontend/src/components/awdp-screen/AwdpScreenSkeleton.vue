<template>
  <div class="awdp-screen-shell">
    <div class="awdp-screen-bg" />
    <div class="awdp-screen-frame">
      <div class="awdp-skeleton h-[5.2rem]" />
      <div class="grid grid-cols-8 gap-2">
        <div v-for="index in 8" :key="index" class="awdp-skeleton h-[4.5rem]" />
      </div>
      <div class="grid min-h-0 grid-cols-[minmax(15.5rem,0.72fr)_minmax(0,1.65fr)_minmax(17rem,0.82fr)] gap-3">
        <div class="awdp-skeleton" />
        <div class="awdp-skeleton" />
        <div class="awdp-skeleton" />
      </div>
      <div class="grid min-h-0 grid-cols-1 gap-3">
        <div class="awdp-skeleton" />
      </div>
    </div>
  </div>
</template>
