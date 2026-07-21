<script setup lang="ts">
import { computed } from 'vue'
import { Bell, Blocks, Command, LayoutDashboard, Palette, Search, UsersRound } from 'lucide-vue-next'
import { useRoute, useRouter } from 'vue-router'
import CommandAdminNav from '../components/CommandAdminNav.vue'
import CommandIconButton from '../primitives/CommandIconButton.vue'
import CommandSignal from '../primitives/CommandSignal.vue'

const route = useRoute()
const router = useRouter()
const workspaceNavigation = [
  { label: 'Overview', icon: LayoutDashboard, to: '/' },
  { label: 'Competitions', icon: Blocks, to: '/competitions' },
  { label: 'Teams', icon: UsersRound, to: '/teams' },
]
const systemNavigation = [
  { label: 'Theme packages', icon: Palette, to: '/admin/theme-packs' },
]

const isAdminRoute = computed(() => String(route.name ?? '').startsWith('admin-'))

const activeItem = computed(() => {
  const all = [...workspaceNavigation, ...systemNavigation]
  if (route.name === 'competitions' || route.name === 'competition-detail' || route.name === 'competition-register'
    || route.name === 'awd-dashboard' || route.name === 'koh-dashboard' || route.name === 'penetration-dashboard')
    return workspaceNavigation[1]!
  if (route.name === 'teams')
    return workspaceNavigation[2]!
  return all.find(item => item.to === route.path) ?? workspaceNavigation[0]!
})
const sectionTitle = computed(() => isAdminRoute.value ? 'ADMIN' : activeItem.value.label.toUpperCase())
const contextSignal = computed(() => {
  if (route.name === 'competitions' || route.name === 'competition-detail' || route.name === 'competition-register'
    || route.name === 'awd-dashboard' || route.name === 'koh-dashboard' || route.name === 'penetration-dashboard')
    return { label: 'Competition API / contract data', tone: 'success' as const }
  if (route.name === 'teams')
    return { label: 'Team API / account data', tone: 'success' as const }
  if (isAdminRoute.value)
    return { label: 'Admin API / operator channel', tone: 'warning' as const }
  if (route.name === 'home')
    return { label: 'Competition API / account overview', tone: 'success' as const }
  return { label: 'Neon Command package', tone: 'info' as const }
})

function navigate(to: string) {
  if (route.path !== to)
    router.push(to)
}
</script>

<template>
  <div class="ui-v2 v2-deck">
    <header class="v2-topbar">
      <div class="v2-topbar__brand" @click="navigate('/')">
        <img src="/logo.png" alt="NoCTF" />
        <div class="v2-topbar__brand-text">
          <strong>NoCTF</strong>
          <span>Command Suite</span>
        </div>
      </div>

      <nav class="v2-topbar__nav" aria-label="Command navigation">
        <CommandIconButton
          v-for="item in workspaceNavigation"
          :key="item.label"
          :icon="item.icon"
          :label="item.label"
          :active="item === activeItem"
          @click="navigate(item.to)"
        />
        <span class="v2-topbar__divider" aria-hidden="true" />
        <CommandIconButton
          v-for="item in systemNavigation"
          :key="item.label"
          :icon="item.icon"
          :label="item.label"
          :active="item === activeItem"
          @click="navigate(item.to)"
        />
      </nav>

      <div class="v2-topbar__actions">
        <CommandIconButton :icon="Search" label="Search" compact />
        <CommandIconButton :icon="Bell" label="Notifications" compact />
        <span class="v2-topbar__operator">OP-01</span>
      </div>
    </header>

    <div class="v2-context">
      <div class="v2-context__inner">
        <div class="v2-context__title">
          <Command class="size-3.5 text-[var(--v2-primary)]" />
          <span>{{ sectionTitle }}</span>
        </div>
        <CommandAdminNav v-if="isAdminRoute" class="v2-context__admin" />
        <div class="v2-context__meta">
          <CommandSignal :label="contextSignal.label" :tone="contextSignal.tone" />
          <span class="v2-context__version">Neon Command v0.2.0</span>
        </div>
      </div>
    </div>

    <main class="v2-content">
      <slot />
    </main>
  </div>
</template>

<style scoped>
.v2-deck {
  min-height: 100dvh;
  display: flex;
  flex-direction: column;
}

.v2-topbar {
  position: sticky;
  z-index: 10;
  top: 0;
  display: flex;
  min-height: 64px;
  align-items: center;
  gap: 22px;
  background: var(--v2-canvas);
  padding: 0 22px;
  box-shadow: 0 1px 0 var(--v2-shadow-light), 0 10px 20px rgb(184 188 194 / 0.42);
}

.v2-topbar__brand {
  display: flex;
  flex: none;
  align-items: center;
  gap: 11px;
  border-radius: 12px;
  cursor: pointer;
  padding: 6px 8px;
}

.v2-topbar__brand img { width: 36px; height: auto; filter: brightness(0) opacity(0.72); }
.v2-topbar__brand-text { display: grid; gap: 2px; }
.v2-topbar__brand-text strong { color: var(--v2-text); font-size: 14px; font-weight: 600; letter-spacing: 0.01em; line-height: 1; }
.v2-topbar__brand-text span { color: var(--v2-primary); font-family: var(--v2-font-mono); font-size: 8px; font-weight: 600; letter-spacing: 0.09em; }

.v2-topbar__nav {
  display: flex;
  min-width: 0;
  flex: 1;
  align-items: center;
  gap: 6px;
  overflow-x: auto;
  scrollbar-width: none;
}
.v2-topbar__nav::-webkit-scrollbar { display: none; }

.v2-topbar__divider {
  width: 2px;
  height: 22px;
  flex: none;
  margin: 0 6px;
  border-radius: 999px;
  background: var(--v2-surface);
  box-shadow: inset 1px 1px 2px rgb(184 188 194 / 0.75), inset -1px -1px 2px rgb(255 255 255 / 0.9);
}

.v2-topbar__actions {
  display: flex;
  flex: none;
  align-items: center;
  gap: 8px;
}

.v2-topbar__operator {
  border-radius: 999px;
  padding: 5px 12px;
  background: var(--v2-surface);
  box-shadow: var(--v2-inset);
  color: var(--v2-text-muted);
  font-family: var(--v2-font-mono);
  font-size: 11px;
  font-weight: 600;
}

.v2-context {
  position: sticky;
  z-index: 5;
  top: 64px;
  /* opaque: backdrop-filter blur forces a full backdrop repaint on every
     scroll frame — the main source of scroll jank in this package */
  background: var(--v2-canvas);
  box-shadow: 0 1px 0 var(--v2-shadow-light), 0 6px 12px rgb(184 188 194 / 0.24);
}

.v2-context__inner {
  display: flex;
  min-height: 40px;
  width: min(1440px, 100%);
  align-items: center;
  justify-content: space-between;
  gap: 14px;
  margin: 0 auto;
  padding: 0 clamp(18px, 3vw, 32px);
}

.v2-context__title { display: flex; min-width: 0; align-items: center; gap: 8px; }
.v2-context__title > span { color: var(--v2-text); font-family: var(--v2-font-mono); font-size: 11px; font-weight: 600; letter-spacing: 0.06em; }
.v2-context__meta { display: flex; flex: none; align-items: center; gap: 12px; }
.v2-context__version { color: var(--v2-text-faint); font-family: var(--v2-font-mono); font-size: 9px; letter-spacing: 0.05em; }

.v2-content {
  width: min(1440px, 100%);
  flex: 1;
  margin: 0 auto;
  padding: 26px clamp(18px, 3vw, 32px) 44px;
}

@media (max-width: 960px) {
  .v2-topbar { gap: 12px; padding: 0 14px; }
  .v2-topbar__brand-text, .v2-topbar__divider { display: none; }
  .v2-topbar__nav :deep(.command-icon-button__label) { display: none; }
  .v2-topbar__nav :deep(.command-icon-button) { width: 40px; padding: 0; justify-content: center; }
}

@media (max-width: 680px) {
  .v2-context__version, .v2-context__meta .command-signal { display: none; }
  .v2-topbar__operator { display: none; }
  .v2-content { padding: 16px 14px 32px; }
}
</style>
