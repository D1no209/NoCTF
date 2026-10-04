<script setup lang="ts">
import { toast } from '../../../utils/message-toast'
import type { NoticePayload } from './notice-state'
import NoticeFrame from './NoticeFrame.vue'
defineOptions({ inheritAttrs: false })
const props = defineProps<{ payload: NoticePayload }>()
const Content = () => props.payload.content?.()
</script>

<template>
  <NoticeFrame :destructive="payload.destructive" @dismiss="toast.dismiss(payload.id)"><Content /></NoticeFrame>
</template>
