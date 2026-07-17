<script setup lang="ts">
import { ArrowLeft, Command } from 'lucide-vue-next'
import { useRouter } from 'vue-router'
import CommandButton from '../primitives/CommandButton.vue'

const router = useRouter()

function returnToCompetition() {
  router.back()
}
</script>

<template>
  <div class="ui-v2 v2-public-screen">
    <header class="v2-public-screen__bar">
      <div class="v2-public-screen__brand">
        <img src="/logo.png" alt="NoCTF" />
        <span>LIVE OPERATIONS DISPLAY</span>
      </div>
      <CommandButton label="Return" tone="ghost" @click="returnToCompetition">
        <template #icon>
          <ArrowLeft class="size-4" />
        </template>
      </CommandButton>
    </header>
    <main class="v2-public-screen__content">
      <slot />
    </main>
    <footer class="v2-public-screen__footer">
      <Command class="size-3.5" />
      <span>NOCTF / AWDP TELEMETRY</span>
    </footer>
  </div>
</template>

<style scoped>
.v2-public-screen {
  display: grid;
  min-height: 100dvh;
  grid-template-rows: 48px minmax(0, 1fr) 28px;
}

.v2-public-screen__bar,
.v2-public-screen__footer {
  display: flex;
  align-items: center;
  justify-content: space-between;
  border-color: var(--v2-line);
  background: rgb(5 8 22 / 0.92);
}

.v2-public-screen__bar {
  border-bottom: 1px solid var(--v2-line);
  padding: 0 18px;
}

.v2-public-screen__brand {
  display: flex;
  min-width: 0;
  align-items: center;
  gap: 11px;
}

.v2-public-screen__brand img {
  width: 86px;
  height: auto;
  filter: brightness(0) invert(1) sepia(0.2) saturate(2.2) hue-rotate(173deg);
}

.v2-public-screen__brand span,
.v2-public-screen__footer {
  color: var(--v2-text-muted);
  font-family: ui-monospace, SFMono-Regular, Menlo, monospace;
  font-size: 10px;
  font-weight: 700;
  letter-spacing: 0.08em;
}

.v2-public-screen__content {
  min-width: 0;
  padding: 14px;
}

.v2-public-screen__footer {
  justify-content: flex-start;
  gap: 7px;
  border-top: 1px solid var(--v2-line);
  padding: 0 18px;
}

@media (max-width: 560px) {
  .v2-public-screen__content { padding: 10px; }
  .v2-public-screen__brand span { display: none; }
}
</style>
