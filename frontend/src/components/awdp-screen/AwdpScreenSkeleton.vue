<template>
  <div class="awdp-screen-shell">
    <div class="awdp-screen-bg" />
    <div class="awdp-screen-frame">
      <div class="awdp-skeleton h-[5.4rem]" />
      <div class="grid grid-cols-8 gap-2">
        <div v-for="index in 8" :key="index" class="awdp-skeleton h-[4.15rem]" />
      </div>
      <div class="grid min-h-0 grid-cols-[minmax(17rem,0.78fr)_minmax(0,1.62fr)_minmax(18rem,0.86fr)] gap-3">
        <div class="awdp-skeleton" />
        <div class="awdp-skeleton" />
        <div class="awdp-skeleton" />
      </div>
      <div class="grid min-h-0 grid-cols-[minmax(0,1.45fr)_minmax(21rem,1fr)] gap-3">
        <div class="awdp-skeleton" />
        <div class="awdp-skeleton" />
      </div>
    </div>
  </div>
</template>
