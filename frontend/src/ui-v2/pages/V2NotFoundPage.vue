<script setup lang="ts">
import { Compass, LayoutDashboard } from 'lucide-vue-next'
import { useRouter } from 'vue-router'
import CommandButton from '../primitives/CommandButton.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSignal from '../primitives/CommandSignal.vue'

const router = useRouter()
</script>

<template>
  <section class="v2-not-found">
    <CommandPanel class="v2-not-found__panel" tone="warning">
      <Compass class="size-6 text-[var(--v2-warning)]" />
      <div>
        <CommandSignal label="Route not resolved" tone="warning" />
        <h1>Nothing is registered at this address</h1>
        <p>The requested path does not match any route known to this platform.</p>
      </div>
      <CommandButton label="Back to overview" @click="router.push('/')">
        <template #icon><LayoutDashboard class="size-4" /></template>
      </CommandButton>
    </CommandPanel>
  </section>
</template>

<style scoped>
.v2-not-found__panel {
  display: grid;
  min-height: 240px;
  grid-template-columns: auto minmax(0, 1fr) auto;
  align-items: center;
  gap: 18px;
  padding: 26px;
}

.v2-not-found__panel h1 { margin: 8px 0 0; color: var(--v2-text); font-size: 18px; font-weight: 600; }
.v2-not-found__panel p { margin: 8px 0 0; color: var(--v2-text-muted); font-size: 13px; }

@media (max-width: 640px) {
  .v2-not-found__panel { grid-template-columns: auto minmax(0, 1fr); }
  .v2-not-found__panel :deep(.command-button) { grid-column: 2; justify-self: start; }
}
</style>
