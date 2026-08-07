<script setup lang="ts">
import { RefreshCw, Repeat } from '@lucide/vue'
import { toast } from 'vue-sonner'
import {
  adminPlatformGetDeadLetter,
  adminPlatformListDeadLetters,
  adminPlatformRequeueDeadLetter,
} from '~/api'
import type { NoCtfapiEndpointsAdministrationPlatformDeadLetterResponse } from '~/api'

definePageMeta({ middleware: 'platform-admin' })

type DeadLetter = NoCtfapiEndpointsAdministrationPlatformDeadLetterResponse

const items = ref<DeadLetter[]>([])
const loading = ref(true)
const loadError = ref<string | null>(null)

async function load(): Promise<void> {
  loading.value = true
  loadError.value = null
  const { data, error } = await adminPlatformListDeadLetters({ query: { limit: 200 } })
  loading.value = false
  if (error) {
    loadError.value = parseApiError(error).message
    return
  }
  items.value = data?.items ?? []
}

const detailOpen = ref(false)
const detailLoading = ref(false)
const detail = ref<DeadLetter | null>(null)
const requeuing = ref(false)

async function openDetail(item: DeadLetter): Promise<void> {
  detailOpen.value = true
  detailLoading.value = true
  detail.value = null
  if (!item.messageId) return
  const { data, error } = await adminPlatformGetDeadLetter({ path: { messageId: item.messageId } })
  detailLoading.value = false
  if (error) {
    toast.error(parseApiError(error).message)
    detailOpen.value = false
    return
  }
  detail.value = data ?? null
}

async function requeue(): Promise<void> {
  if (!detail.value?.messageId) return
  requeuing.value = true
  const { error } = await adminPlatformRequeueDeadLetter({ path: { messageId: detail.value.messageId } })
  requeuing.value = false
  if (error) {
    toast.error(parseApiError(error).message)
    return
  }
  toast.success('已重新入队')
  detailOpen.value = false
  await load()
}

onMounted(() => {
  void load()
})
</script>

<template>
  <div class="flex flex-col gap-4">
    <div class="flex items-center justify-between gap-4">
      <p class="text-sm text-muted-foreground">
        Wolverine 死信队列。出于安全考虑,消息正文与异常详情已被后端脱敏,此处仅展示元数据。
      </p>
      <Button variant="outline" :disabled="loading" @click="load">
        <Spinner v-if="loading" data-icon="inline-start" />
        <RefreshCw v-else data-icon="inline-start" />
        刷新
      </Button>
    </div>

    <Alert v-if="loadError" variant="destructive">
      <AlertDescription>{{ loadError }}</AlertDescription>
    </Alert>

    <Card v-if="loading && items.length === 0">
      <CardContent class="flex flex-col gap-3 pt-6">
        <Skeleton v-for="i in 4" :key="i" class="h-10 w-full" />
      </CardContent>
    </Card>

    <Empty v-else-if="items.length === 0">
      <EmptyHeader>
        <EmptyTitle>死信队列为空</EmptyTitle>
        <EmptyDescription>没有投递失败的消息。</EmptyDescription>
      </EmptyHeader>
    </Empty>

    <Card v-else>
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead>消息类型</TableHead>
            <TableHead>来源</TableHead>
            <TableHead>异常类型</TableHead>
            <TableHead>进入时间</TableHead>
            <TableHead>可重放</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          <TableRow v-for="item in items" :key="item.messageId" class="cursor-pointer" @click="openDetail(item)">
            <TableCell class="max-w-64 truncate font-medium" :title="item.messageType">
              {{ item.messageType }}
            </TableCell>
            <TableCell class="text-muted-foreground">{{ item.source }}</TableCell>
            <TableCell>
              <Badge variant="destructive">{{ item.exceptionType }}</Badge>
            </TableCell>
            <TableCell>
              <AdminDateTime :value="item.sentAt" />
            </TableCell>
            <TableCell>
              <Badge :variant="item.replayable ? 'secondary' : 'outline'">
                {{ item.replayable ? '是' : '否' }}
              </Badge>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </Card>

    <Sheet v-model:open="detailOpen">
      <SheetContent>
        <SheetHeader>
          <SheetTitle>死信详情</SheetTitle>
          <SheetDescription class="font-mono text-xs break-all">{{ detail?.messageId ?? '加载中…' }}</SheetDescription>
        </SheetHeader>
        <div v-if="detailLoading" class="flex flex-col gap-3 px-4">
          <Skeleton v-for="i in 4" :key="i" class="h-8 w-full" />
        </div>
        <div v-else-if="detail" class="flex flex-col gap-6 px-4 pb-6">
          <dl class="grid grid-cols-2 gap-x-4 gap-y-3 text-sm">
            <dt class="text-muted-foreground">消息类型</dt>
            <dd class="break-all">{{ detail.messageType }}</dd>
            <dt class="text-muted-foreground">来源</dt>
            <dd>{{ detail.source }}</dd>
            <dt class="text-muted-foreground">异常类型</dt>
            <dd class="break-all">{{ detail.exceptionType }}</dd>
            <dt class="text-muted-foreground">进入时间</dt>
            <dd><AdminDateTime :value="detail.sentAt" /></dd>
            <dt class="text-muted-foreground">可重放</dt>
            <dd>{{ detail.replayable ? '是' : '否' }}</dd>
          </dl>
          <Alert>
            <AlertDescription>
              消息正文(payload)与异常详情由后端刻意脱敏,不对外暴露;如需排查请查看平台日志。
            </AlertDescription>
          </Alert>
          <div>
            <AlertDialog>
              <AlertDialogTrigger as-child>
                <Button :disabled="!detail.replayable">
                  <Repeat data-icon="inline-start" />
                  重新入队
                </Button>
              </AlertDialogTrigger>
              <AlertDialogContent>
                <AlertDialogHeader>
                  <AlertDialogTitle>重新入队</AlertDialogTitle>
                  <AlertDialogDescription>
                    将把该死信消息重新投递到消息队列处理。若失败原因未消除,可能再次进入死信队列。
                  </AlertDialogDescription>
                </AlertDialogHeader>
                <AlertDialogFooter>
                  <AlertDialogCancel>取消</AlertDialogCancel>
                  <AlertDialogAction :disabled="requeuing" @click="requeue">
                    <Spinner v-if="requeuing" data-icon="inline-start" />
                    确认重放
                  </AlertDialogAction>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>
            <p v-if="!detail.replayable" class="mt-2 text-sm text-muted-foreground">
              该消息不可重放。
            </p>
          </div>
        </div>
      </SheetContent>
    </Sheet>
  </div>
</template>
