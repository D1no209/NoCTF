<script setup lang="ts">
import { computed } from 'vue'
import PixelBlast from '@/ui-v1/components/PixelBlast.vue'
import TargetCursor from '@/ui-v1/components/TargetCursor.vue'
import { Toaster } from '@/ui-v1/components/ui/sonner'
import { useThemePackages } from '@/composables/useThemePackages'

// V1 theme package application root: router view plus the package's ambient
// chrome (pixel backdrop, target cursor, toast outlet). All page behavior
// lives in src/features — this root only renders.
const { activeTheme } = useThemePackages()

const effectColor = computed(() => activeTheme.value.tokens['--app-effect-color'] || activeTheme.value.tokens['--primary'])
const effectOpacity = computed(() => Number(activeTheme.value.tokens['--app-effect-opacity'] || '0.28'))
</script>

<template>
  <div class="app-pixel-shell">
    <div class="app-pixel-bg" :style="{ opacity: effectOpacity }" aria-hidden="true">
      <PixelBlast
        variant="square"
        :pixel-size="6"
        :color="effectColor"
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
  pointer-events: none;
  background: linear-gradient(180deg, var(--app-background-start) 0%, var(--app-background-end) 100%);
}

.app-pixel-content {
  position: relative;
  z-index: 1;
}
</style>
