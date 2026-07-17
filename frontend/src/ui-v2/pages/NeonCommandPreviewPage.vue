<script setup lang="ts">
import { RefreshCw } from 'lucide-vue-next'
import { onMounted, ref } from 'vue'
import CommandEventFeed from '../components/CommandEventFeed.vue'
import CommandMetricStrip from '../components/CommandMetricStrip.vue'
import CommandNetworkTrace from '../components/CommandNetworkTrace.vue'
import CommandPageHeader from '../components/CommandPageHeader.vue'
import CommandRankTable from '../components/CommandRankTable.vue'
import CommandServiceMatrix from '../components/CommandServiceMatrix.vue'
import CommandBadge from '../primitives/CommandBadge.vue'
import CommandIconButton from '../primitives/CommandIconButton.vue'
import CommandPanel from '../primitives/CommandPanel.vue'
import CommandSignal from '../primitives/CommandSignal.vue'
import { loadSharedThemePreviewMock, type CommandPreviewModel } from '../mock/shared-theme-mock'

const preview = ref<CommandPreviewModel | null>(null)
const isRefreshing = ref(false)

async function refreshPreview() {
  isRefreshing.value = true
  try {
    preview.value = await loadSharedThemePreviewMock()
  }
  finally {
    isRefreshing.value = false
  }
}

onMounted(refreshPreview)
</script>

<template>
  <template v-if="preview">
    <CommandPageHeader
      signal-label="NoCTF / Operational overview"
      signal-tone="success"
      title="Competition command center"
      description="Shared development mock from the first UI package, rendered through the independent Neon Command UI package."
    >
      <CommandBadge :label="`Snapshot ${preview.snapshotTime}`" tone="info" />
      <CommandIconButton :icon="RefreshCw" :label="isRefreshing ? 'Refreshing mock' : 'Refresh mock'" compact @click="refreshPreview" />
    </CommandPageHeader>

    <CommandMetricStrip :metrics="preview.metrics" />

    <div class="command-dashboard">
      <div class="command-dashboard__primary">
        <CommandNetworkTrace :trace="preview.trace" />
        <CommandEventFeed :events="preview.events" />
      </div>
      <div class="command-dashboard__secondary">
        <CommandRankTable :teams="preview.teams" />
        <CommandServiceMatrix :services="preview.services" />
      </div>
    </div>
  </template>

  <CommandPanel v-else class="command-preview-state">
    <CommandSignal label="Development preview unavailable" tone="warning" />
    <h1>Shared mock is only available in development.</h1>
  </CommandPanel>
</template>

<style scoped>
.command-dashboard {
  display: grid;
  grid-template-columns: minmax(0, 1.2fr) minmax(380px, 0.8fr);
  gap: 18px;
  margin-top: 18px;
}

.command-dashboard__primary,
.command-dashboard__secondary {
  display: grid;
  min-width: 0;
  gap: 18px;
}

.command-dashboard__primary { grid-template-rows: minmax(300px, 0.9fr) minmax(350px, 1.1fr); }
.command-dashboard__secondary { grid-template-rows: minmax(330px, 1fr) minmax(300px, 0.9fr); }

.command-preview-state {
  display: grid;
  min-height: 220px;
  place-content: center;
  gap: 10px;
  padding: 26px;
}

.command-preview-state h1 {
  margin: 0;
  color: var(--v2-text);
  font-size: 17px;
  font-weight: 600;
}

@media (max-width: 1120px) {
  .command-dashboard { grid-template-columns: 1fr; }
  .command-dashboard__primary,
  .command-dashboard__secondary { grid-template-rows: auto; }
}
</style>
