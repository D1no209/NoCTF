<script setup lang="ts">
import { Check, Copy, Pencil, Trash2 } from 'lucide-vue-next'
import { Badge } from '@/ui-v1/components/ui/badge'
import { Button } from '@/ui-v1/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/ui-v1/components/ui/card'
import type { ThemePackage } from '@/themes/theme-package'

const props = defineProps<{
  theme: ThemePackage
  active: boolean
}>()

const emit = defineEmits<{
  apply: [id: string]
  duplicate: [id: string]
  edit: [id: string]
  remove: [id: string]
}>()
</script>

<template>
  <Card :decorated="false" class="overflow-hidden">
    <div class="grid h-20 grid-cols-4 border-b-2 border-card-border">
      <div
        v-for="color in [props.theme.tokens['--background'], props.theme.tokens['--card'], props.theme.tokens['--primary'], props.theme.tokens['--accent']]"
        :key="color"
        :style="{ backgroundColor: color }"
        class="min-w-0"
      />
    </div>

    <CardHeader class="gap-3 pt-4">
      <div class="flex items-start justify-between gap-3">
        <div class="min-w-0">
          <CardTitle class="truncate text-base">{{ props.theme.name }}</CardTitle>
          <p class="mt-1 line-clamp-2 min-h-10 text-xs leading-5 text-muted-foreground">
            {{ props.theme.description }}
          </p>
        </div>
        <Badge :variant="props.active ? 'default' : 'outline'" class="shrink-0 text-[10px]">
          {{ props.active ? 'Active' : props.theme.builtIn ? 'Built-in' : 'Local' }}
        </Badge>
      </div>
      <div class="flex items-center justify-between gap-3 text-[10px] text-muted-foreground">
        <span>v{{ props.theme.version }} / {{ props.theme.uiPackage.toUpperCase() }}</span>
        <span>{{ props.theme.builtIn ? 'Read-only source' : 'Browser local' }}</span>
      </div>
    </CardHeader>

    <CardContent class="mt-auto flex items-center gap-2 pt-0">
      <Button
        class="min-w-0 flex-1"
        :variant="props.active ? 'secondary' : 'default'"
        :disabled="props.active"
        @click="emit('apply', props.theme.id)"
      >
        <Check class="size-4" />
        {{ props.active ? 'Applied' : 'Apply' }}
      </Button>
      <Button
        variant="outline"
        size="icon"
        :title="`Duplicate ${props.theme.name}`"
        :aria-label="`Duplicate ${props.theme.name}`"
        @click="emit('duplicate', props.theme.id)"
      >
        <Copy class="size-4" />
      </Button>
      <Button
        variant="ghost"
        size="icon"
        :title="props.theme.builtIn ? `Duplicate and edit ${props.theme.name}` : `Edit ${props.theme.name}`"
        :aria-label="props.theme.builtIn ? `Duplicate and edit ${props.theme.name}` : `Edit ${props.theme.name}`"
        @click="emit('edit', props.theme.id)"
      >
        <Pencil class="size-4" />
      </Button>
      <Button
        v-if="!props.theme.builtIn"
        variant="ghost"
        size="icon"
        :title="`Delete ${props.theme.name}`"
        :aria-label="`Delete ${props.theme.name}`"
        @click="emit('remove', props.theme.id)"
      >
        <Trash2 class="size-4 text-destructive" />
      </Button>
    </CardContent>
  </Card>
</template>
