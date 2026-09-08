<script setup lang="ts">
import { toRefs } from 'vue'
import type { WorkspaceNavMenuViewState } from '~/features/app/useWorkspaceNavMenu'

const viewProps = defineProps<{ state: WorkspaceNavMenuViewState }>()
const { isActive, onNavigate, groups } = toRefs(viewProps.state)
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
