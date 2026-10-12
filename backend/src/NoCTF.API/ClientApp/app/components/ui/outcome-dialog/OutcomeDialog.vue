<script setup lang="ts">
import { Trophy, Minus } from '@lucide/vue'

const props = defineProps<{
  open: boolean; heading: string; winner: string; winnerLabel: string; hasWinner: boolean; avatar?: string | null;
  scoreLabel: string; leftName: string; rightName: string; leftScore?: number | null; rightScore?: number | null; missingScoreLabel: string;
  reason: string; corrected?: string | null; continueLabel: string; backLabel: string; nextLabel?: string | null; busy?: boolean; returnFocusId?: string
}>()
const emit = defineEmits<{ 'update:open': [value: boolean]; back: []; next: [] }>()
function restoreFocus(event: Event) {
  const target = props.returnFocusId ? document.getElementById(props.returnFocusId) : null
  if (target instanceof HTMLElement) { event.preventDefault(); target.focus({ preventScroll: true }) }
}
</script>
<template>
  <Dialog :open="open" @update:open="emit('update:open', $event)">
    <DialogContent class="flex max-h-[calc(100dvh-2rem)] flex-col sm:max-w-xl" @close-auto-focus="restoreFocus">
      <DialogHeader class="shrink-0 pr-8">
        <DialogTitle>{{ heading }}</DialogTitle>
        <DialogDescription class="sr-only">{{ reason }}</DialogDescription>
      </DialogHeader>
      <ScrollSurface axis="y" class="flex min-h-0 min-w-0 flex-1 flex-col gap-6 py-2 sm:py-4" role="status">
        <div class="flex min-w-0 flex-col items-center gap-3 text-center">
          <Badge v-if="corrected" variant="secondary">{{ corrected }}</Badge>
          <p v-if="hasWinner" class="inline-flex items-center gap-2 text-sm font-semibold text-primary"><Trophy class="size-4" aria-hidden="true" />{{ winnerLabel }}</p>
          <Avatar v-if="hasWinner" class="size-20 sm:size-24"><AvatarImage v-if="avatar" :src="avatar" :alt="winner" /><AvatarFallback>{{ winner.slice(0, 2) }}</AvatarFallback></Avatar>
          <Minus v-else class="size-12 text-muted-foreground" aria-hidden="true" />
          <p class="max-w-full break-words text-2xl font-bold leading-snug sm:text-3xl">{{ winner }}</p>
          <p class="max-w-[55ch] text-sm leading-relaxed text-muted-foreground">{{ reason }}</p>
        </div>
        <section class="min-w-0 rounded-lg bg-muted/50 px-4 py-4 sm:px-6">
          <p class="mb-4 text-center text-xs font-medium text-muted-foreground">{{ scoreLabel }}</p>
          <dl class="grid min-w-0 grid-cols-2 gap-x-6 gap-y-2 text-center">
            <div class="row-span-2 grid min-w-0 grid-rows-subgrid gap-2"><dt class="break-words text-sm font-medium leading-relaxed">{{ leftName }}</dt><dd class="font-mono text-4xl font-bold tabular-nums sm:text-5xl">{{ leftScore ?? '—' }}</dd></div>
            <div class="row-span-2 grid min-w-0 grid-rows-subgrid gap-2"><dt class="break-words text-sm font-medium leading-relaxed">{{ rightName }}</dt><dd class="font-mono text-4xl font-bold tabular-nums sm:text-5xl">{{ rightScore ?? '—' }}</dd></div>
          </dl>
          <p v-if="leftScore == null || rightScore == null" class="mt-3 text-center text-xs text-muted-foreground">{{ missingScoreLabel }}</p>
        </section>
      </ScrollSurface>
      <DialogFooter class="shrink-0 flex-wrap gap-2">
        <Button variant="ghost" @click="emit('back')">{{ backLabel }}</Button>
        <Button :variant="nextLabel ? 'outline' : 'default'" @click="emit('update:open', false)">{{ continueLabel }}</Button>
        <Button v-if="nextLabel" :disabled="busy" @click="emit('next')"><Spinner v-if="busy" />{{ nextLabel }}</Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
