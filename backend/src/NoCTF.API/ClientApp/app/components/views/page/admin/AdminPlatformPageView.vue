<script setup lang="ts">
import { toRefs } from 'vue'
import type { AdminPlatformPageViewState } from '~/features/routes/admin/useAdminPlatformPage'

const viewProps = defineProps<{ state: AdminPlatformPageViewState }>()
const { navGroups, activePath, AppWorkspaceNav } = toRefs(viewProps.state)
</script>

<template>
  <component :is="AppWorkspaceNav" :groups="navGroups" :title="$t('ui.platformAdmin')">
    <div data-workspace-scroll-content data-platform-workspace-content class="mx-auto flex h-full min-h-0 w-full max-w-6xl flex-col gap-6 px-4 pt-8 md:px-6">
      <h1 class="shrink-0 text-display text-2xl">{{ $t('ui.platformAdmin') }}</h1>
      <ScrollSurface axis="y" :reset-key="activePath" class="min-h-0 flex-1 overscroll-contain pr-3" :aria-label="$t('ui.platformAdmin')">
        <div class="px-1 pb-8">
          <MotionSwap :identity="activePath" preset="film-up">
            <NuxtPage />
          </MotionSwap>
        </div>
      </ScrollSurface>
    </div>
  </component>
</template>
