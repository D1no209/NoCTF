<script setup lang="ts">
import { toRefs } from 'vue'
import type { AppWorkspaceNavViewState } from '~/features/app/useAppWorkspaceNav'

const viewProps = defineProps<{ state: AppWorkspaceNavViewState }>()
const { WorkspaceNavMenu, groups, title } = toRefs(viewProps.state)
</script>

<template>
  <SidebarProvider class="min-h-[calc(100svh-4rem)]">
    <!-- 桌面端把 Sidebar 的 fixed 全屏定位覆盖为吸顶于全局页头(h-16)之下;移动端仍走 Sheet 抽屉 -->
    <Sidebar collapsible="icon" class="md:sticky md:top-16 md:bottom-auto md:h-[calc(100svh-4rem)]">
      <SidebarHeader v-if="title" class="flex-row items-center gap-1 px-3 py-3">
        <span class="min-w-0 flex-1 truncate text-sm font-semibold group-data-[collapsible=icon]:hidden">
          {{ title }}
        </span>
        <SidebarTrigger class="ml-auto shrink-0" />
      </SidebarHeader>
      <SidebarSeparator v-if="title" class="mx-0" />
      <SidebarContent>
        <component :is="WorkspaceNavMenu" :groups="groups" />
      </SidebarContent>
    </Sidebar>
    <SidebarInset class="min-w-0">
      <div class="flex items-center gap-2 border-b px-3 py-2 md:hidden">
        <SidebarTrigger />
        <span v-if="title" class="truncate text-sm font-semibold">{{ title }}</span>
      </div>
      <slot />
    </SidebarInset>
  </SidebarProvider>
</template>
