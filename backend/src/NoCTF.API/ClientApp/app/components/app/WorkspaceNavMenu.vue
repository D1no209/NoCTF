<script setup lang="ts">
import type { WorkspaceNavGroup, WorkspaceNavItem } from './workspace-nav'
import { useSidebar } from '@/components/ui/sidebar'

defineProps<{
  groups: WorkspaceNavGroup[]
}>()

const route = useRoute()
const { isMobile, setOpenMobile } = useSidebar()

function isActive(item: WorkspaceNavItem) {
  return item.exact
    ? route.path === item.to
    : route.path === item.to || route.path.startsWith(`${item.to}/`)
}

function onNavigate() {
  if (isMobile.value) setOpenMobile(false)
}
</script>

<template>
  <SidebarGroup v-for="(group, index) in groups" :key="group.label ?? index">
    <SidebarGroupLabel v-if="group.label" class="text-[11px] uppercase tracking-[0.14em]">{{ group.label }}</SidebarGroupLabel>
    <SidebarGroupContent>
      <SidebarMenu>
        <SidebarMenuItem v-for="item in group.items" :key="item.to">
          <SidebarMenuButton
            as-child
            :is-active="isActive(item)"
            :tooltip="item.label"
            class="data-[active]:font-medium data-[active]:shadow-[inset_2px_0_0_0_var(--sidebar-primary)]"
          >
            <NuxtLink :to="item.to" @click="onNavigate">
              <component :is="item.icon" />
              <span>{{ item.label }}</span>
            </NuxtLink>
          </SidebarMenuButton>
        </SidebarMenuItem>
      </SidebarMenu>
    </SidebarGroupContent>
  </SidebarGroup>
</template>
