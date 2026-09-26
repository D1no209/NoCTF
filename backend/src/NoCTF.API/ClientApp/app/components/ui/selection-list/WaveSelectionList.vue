<script setup lang="ts" generic="T extends { value: string; label: string }">
import { ref } from 'vue'
import { ChevronDown } from '@lucide/vue'
import { RovingFocusGroup } from 'reka-ui'
import { useWaveMotion } from '~/motion/useWaveMotion'
import { Button } from '../button'
import ScrollSurface from '../scroll-area/ScrollSurface.vue'
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '../collapsible'
import { useDisclosureGroups } from './useDisclosureGroups'
import WaveSelectionItem from './WaveSelectionItem.vue'

defineOptions({ inheritAttrs: false })
const props = defineProps<{ items: T[]; groups?: { value: string; label: string; items: T[] }[]; modelValue: string | null; label: string; controls?: string; compact?: boolean }>()
const emit = defineEmits<{ 'update:modelValue': [value: string] }>()
const surface = ref<HTMLElement | null>(null)
const wave = useWaveMotion(surface, () => props.modelValue)
const { isOpen, setOpen } = useDisclosureGroups(() => props.groups, () => props.modelValue)
</script>

<template>
  <div ref="surface" v-bind="$attrs" data-slot="wave-list-surface" @pointermove="wave.onPointerMove" @pointerleave="wave.onPointerLeave" @pointercancel="wave.onPointerLeave" @focusin="wave.onFocusIn" @focusout="wave.onFocusOut" @scroll.capture="wave.onScroll">
    <ScrollSurface axis="y" class="h-full" :aria-label="label">
      <RovingFocusGroup orientation="vertical" loop :role="groups?.length ? 'group' : 'listbox'" :aria-label="label" data-slot="wave-selection-list" :data-grouped="Boolean(groups?.length)">
        <template v-if="groups?.length">
          <Collapsible v-for="group in groups" :key="group.value" :open="isOpen(group.value)" @update:open="setOpen(group.value, $event)">
            <CollapsibleTrigger as-child>
              <Button variant="ghost" data-slot="wave-group-trigger">
                <slot name="group" :group="group">{{ group.label }}</slot>
                <span class="ml-auto text-xs text-muted-foreground">{{ group.items.length }}</span>
                <ChevronDown class="noctf-disclosure-chevron size-4" aria-hidden="true" />
              </Button>
            </CollapsibleTrigger>
            <CollapsibleContent class="noctf-disclosure-content" role="listbox" :aria-label="group.label">
              <div data-slot="wave-group-items">
                <WaveSelectionItem v-for="item in group.items" :key="item.value" :value="item.value" :label="item.label" :selected="modelValue === item.value" :controls="controls" :hint="compact ? item.label : undefined" @select="emit('update:modelValue', $event)">
                  <slot :item="item"><span>{{ item.label }}</span></slot>
                </WaveSelectionItem>
              </div>
            </CollapsibleContent>
          </Collapsible>
        </template>
        <template v-else>
          <WaveSelectionItem v-for="item in items" :key="item.value" :value="item.value" :label="item.label" :selected="modelValue === item.value" :controls="controls" :hint="compact ? item.label : undefined" @select="emit('update:modelValue', $event)">
            <slot :item="item"><span>{{ item.label }}</span></slot>
          </WaveSelectionItem>
        </template>
      </RovingFocusGroup>
    </ScrollSurface>
  </div>
</template>
