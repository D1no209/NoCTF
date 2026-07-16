<script setup lang="ts">
import PixelBlast from '@/components/PixelBlast.vue'
import TargetCursor from '@/components/TargetCursor.vue'
import { Toaster } from '@/components/ui/sonner'
</script>

<template>
  <div class="app-pixel-shell">
    <div class="app-pixel-bg" aria-hidden="true">
      <PixelBlast
        variant="square"
        :pixel-size="6"
        color="#2f2f2f"
        :pattern-scale="2.6"
        :pattern-density="1.05"
        :pixel-size-jitter="0.22"
        :enable-ripples="true"
        :ripple-speed="0.28"
        :ripple-thickness="0.1"
        :ripple-intensity-scale="0.7"
        :liquid="false"
        :speed="0.42"
        :edge-fade="0.12"
        :transparent="true"
      />
    </div>

    <div class="app-pixel-content">
      <RouterView />
      <Toaster position="top-right" rich-colors close-button />
    </div>
    <TargetCursor
      target-selector=".cursor-target, button, a, input, select, textarea, [role='button']"
      :spin-duration="2.4"
      :hide-default-cursor="true"
      :hover-duration="0.18"
      :parallax-on="true"
    />
  </div>
</template>

<style scoped>
.app-pixel-shell {
  position: relative;
  min-height: 100dvh;
}

.app-pixel-bg {
  position: fixed;
  inset: 0;
  z-index: 0;
  opacity: 0.28;
  pointer-events: none;
  background: linear-gradient(180deg, #e7e7e7 0%, #dfdfdf 100%);
}

.app-pixel-content {
  position: relative;
  z-index: 1;
}
</style>
