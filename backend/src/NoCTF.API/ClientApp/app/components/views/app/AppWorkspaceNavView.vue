<script setup lang="ts">
import { toRefs } from 'vue'
import type { AppWorkspaceNavViewState } from '~/features/app/useAppWorkspaceNav'

const viewProps = defineProps<{ state: AppWorkspaceNavViewState }>()
const { title, groupOptions, options, selectedPath, selectPath } = toRefs(viewProps.state)
</script>

<template>
  <div data-slot="app-workspace-nav" class="settings-workspace-page">
    <div class="settings-workspace-layout">
      <div class="min-w-0">
        <ChoiceSidebar :groups="groupOptions" :items="options" :model-value="selectedPath" :label="title || $t('ui.mainNavigation')" :loading-label="title || $t('ui.mainNavigation')" :empty-label="title || $t('ui.mainNavigation')" controls="settings-workspace-content" @update:model-value="selectPath">
          <template #header>
            <header class="pr-10">
              <h2 class="truncate text-base font-semibold">{{ title }}</h2>
            </header>
          </template>
          <template #item="{ item }">
            <span class="flex min-w-0 items-center gap-3">
              <component :is="item.item.icon" class="size-4 shrink-0" />
              <span class="min-w-0 truncate text-sm font-medium">{{ item.label }}</span>
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
