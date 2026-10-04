<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import type { AlertVariants } from '~/components/ui/alert'
import { markRaw, nextTick, onBeforeUnmount, onMounted, onUpdated, ref, shallowReactive, useId, useSlots, watch } from 'vue'
import { toast } from '../../../utils/message-toast'
import NoticeToast from '../sonner/NoticeToast.vue'
import type { NoticePayload } from '../sonner/notice-state'

defineOptions({ inheritAttrs: false })

const props = defineProps<{
  class?: HTMLAttributes['class']
  variant?: AlertVariants['variant']
}>()
const slots = useSlots()
const id = `notice-${useId()}`
let active = false
const anchor = ref<HTMLElement | null>(null)
let visible = false
let observer: ResizeObserver | undefined
const payload = shallowReactive<NoticePayload>({ id, destructive: props.variant === 'destructive', content: slots.default })
function publish() {
  if (!active || !visible) return
  payload.destructive = props.variant === 'destructive'
  toast.custom(markRaw(NoticeToast), {
    id,
    duration: Number.POSITIVE_INFINITY,
    position: 'bottom-right',
    class: props.variant === 'destructive' ? 'noctf-notice-error' : 'noctf-notice-info',
    componentProps: { payload },
  })
}
function refreshVisibility() {
  const next = Boolean(anchor.value?.getClientRects().length)
  if (next === visible) return
  visible = next
  if (visible) publish()
  else toast.dismiss(id)
}
onMounted(() => {
  active = true
  observer = new ResizeObserver(refreshVisibility)
  if (anchor.value) observer.observe(anchor.value)
  void nextTick(refreshVisibility)
})
watch(() => props.variant, publish)
onUpdated(() => { payload.content = slots.default })
onBeforeUnmount(() => {
  active = false
  observer?.disconnect()
  // Sonner keeps history: release the slot so it cannot retain an unmounted feature and its DOM.
  payload.content = undefined
  toast.dismiss(id)
})
</script>

<template>
  <span ref="anchor" class="notice-anchor" aria-hidden="true" />
</template>
