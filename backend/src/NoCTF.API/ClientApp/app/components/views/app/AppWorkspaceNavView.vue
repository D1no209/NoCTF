<script setup lang="ts">
import { toRefs } from 'vue'
import type { AppWorkspaceNavViewState } from '~/features/app/useAppWorkspaceNav'

const viewProps = defineProps<{ state: AppWorkspaceNavViewState }>()
const {
  title, groupOptions, options, selectedPath, selectPath,
  isDesktop, isCollapsed, toggleCollapsed, CollapseIcon, ExpandIcon,
} = toRefs(viewProps.state)
</script>

<template>
  <div data-slot="app-workspace-nav" class="settings-workspace-page" :data-collapsed="isCollapsed ? 'true' : undefined">
    <div class="settings-workspace-layout">
      <div data-workspace-nav-sidebar class="min-w-0">
        <ChoiceSidebar :groups="isCollapsed ? undefined : groupOptions" :compact="isCollapsed" :items="options" :model-value="selectedPath" :label="title || $t('common.label.mainNavigation')" :loading-label="title || $t('common.label.mainNavigation')" :empty-label="title || $t('common.label.mainNavigation')" controls="settings-workspace-content" @update:model-value="selectPath">
          <template #header>
            <header class="flex min-h-11 items-center justify-between gap-2 pr-10" data-workspace-nav-header>
              <h2 v-if="!isCollapsed" class="min-w-0 truncate text-base font-semibold">{{ title }}</h2>
              <Button v-if="isDesktop" type="button" variant="ghost" size="icon" class="size-11 shrink-0"
                :aria-label="$t(isCollapsed ? 'common.label.expandMenu' : 'common.label.collapseMenu')"
                :aria-expanded="!isCollapsed" @click="toggleCollapsed">
                <component :is="isCollapsed ? ExpandIcon : CollapseIcon" data-icon="inline-start" />
              </Button>
            </header>
          </template>
          <template #item="{ item }">
            <span class="flex min-w-0 items-center gap-3">
              <component :is="item.item.icon" class="size-4 shrink-0" />
              <span v-if="!isCollapsed" class="min-w-0 truncate text-sm font-medium">{{ item.label }}</span>
            </span>
          </template>
        </ChoiceSidebar>
      </div>
      <main id="settings-workspace-content" class="min-w-0">
      <slot />
      </main>
    </div>
  </div>
</template>

<style src="./settings-workspace.css"></style>
