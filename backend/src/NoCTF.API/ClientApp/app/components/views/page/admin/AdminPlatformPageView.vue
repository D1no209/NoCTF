<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminPlatformPageViewState } from '~/features/routes/admin/useAdminPlatformPage'

const viewProps = defineProps<{ state: AdminPlatformPageViewState }>()
const { navGroups, AppWorkspaceNav } = toRefs(viewProps.state)
</script>

<template>
  <component :is="AppWorkspaceNav" :groups="navGroups" :title="$t('ui.platformAdmin')">
    <div class="mx-auto flex w-full max-w-6xl flex-col gap-6 px-4 py-8 md:px-6">
      <div>
        <h1 class="text-display text-2xl">{{ $t('ui.platformAdmin') }}</h1>
        <p class="text-sm text-muted-foreground">{{ $t('ui.platformConfigurationUsersLogsAndOperationAndMaintenanceManagement') }}</p>
      </div>
      <NuxtPage />
    </div>
  </component>
</template>
