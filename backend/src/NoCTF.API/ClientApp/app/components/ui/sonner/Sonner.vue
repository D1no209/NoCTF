<script lang="ts" setup>
import type { ToasterProps } from 'vue-sonner'

import { Loader2Icon, XIcon } from '@lucide/vue'
import NoticeIcon from './NoticeIcon.vue'
import { reactiveOmit } from '@vueuse/core'
import { Toaster as Sonner } from 'vue-sonner'
import { cn } from '~/lib/utils'
import { onBeforeUnmount, onMounted, shallowRef } from 'vue'

const props = defineProps<ToasterProps>()
const delegatedProps = reactiveOmit(props, 'class', 'toastOptions', 'containerAriaLabel')
const target = shallowRef<Element | string>('body')
function updateTarget() { target.value = document.fullscreenElement ?? 'body' }
onMounted(() => { updateTarget(); document.addEventListener('fullscreenchange', updateTarget) })
onBeforeUnmount(() => document.removeEventListener('fullscreenchange', updateTarget))
</script>

<template>
  <Teleport :to="target">
  <Sonner
    :container-aria-label="props.containerAriaLabel ?? $t('ui.notifications')"
    :class="cn('toaster group', props.class)"
    :style="{
      '--normal-bg': 'var(--popover)',
      '--normal-text': 'var(--popover-foreground)',
      '--normal-border': 'var(--border)',
      '--border-radius': 'var(--radius)',
      '--gray2': 'hsl(var(--popover) / 0.9)',
      '--gray3': 'var(--border)',
      '--gray4': 'var(--border)',
      '--gray5': 'var(--border)',
      '--gray12': 'var(--popover-foreground)',
    }"
    :toast-options="{
      closeButtonAriaLabel: $t('ui.close'),
      ...(props.toastOptions ?? { classes: { toast: 'rounded-2xl' } }),
    }"
    v-bind="delegatedProps"
  >
    <template #success-icon>
      <NoticeIcon tone="success" />
    </template>
    <template #info-icon>
      <NoticeIcon tone="info" />
    </template>
    <template #warning-icon>
      <NoticeIcon tone="warning" />
    </template>
    <template #error-icon>
      <NoticeIcon tone="error" />
    </template>
    <template #loading-icon>
      <div>
        <Loader2Icon class="size-4 animate-spin" />
      </div>
    </template>
    <template #close-icon>
      <XIcon class="size-4" />
    </template>
  </Sonner>
  </Teleport>
</template>
